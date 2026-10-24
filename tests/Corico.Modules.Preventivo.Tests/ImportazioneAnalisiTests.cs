using Corico.Core.Dominio;
using System.Text;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;
using Xunit.Abstractions;

namespace Corico.Modules.Preventivo.Tests;

public class ImportazioneAnalisiTests(ITestOutputHelper output)
{
    private const string Intestazione = "Codice;Descrizione;Unita;Stato;Fonte;Note;CostoUnitario";
    private const string IntestazioneRighe = "CodiceAnalisi;Ordine;Categoria;CategoriaNumero;Descrizione;Unita;Fabbisogno;SfridoPercentuale;Prezzo;Fonte;Note;CodiceRisorsa";

    private static FileCaricato Csv(string nome, params string[] righe) =>
        new(nome, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", righe) + "\r\n")).ToArray());

    /// <summary>Tre analisi nel formato del tracciato SIC24: scavo e intonaco confermati, massetto in bozza.</summary>
    private static List<FileCaricato> Tracciato(decimal prezzoMalta = 150, decimal prezzoEscavatore = 86.26m) =>
    [
        Csv("Analisi.csv", Intestazione,
            "SIC24_1.1.1.1;Scavo di sbancamento;m³;Confermata;Analisi costo SIC24;Nota scavo;4.10",
            "SIC24_9.1.1;Intonaco civile;m²;Confermata;Analisi costo SIC24;;18.30",
            "SIC24_9.1.10;Massetto;m²;Bozza;Analisi costo SIC24;\"Anomalia media; da verificare\";5"),
        Csv("RigheAnalisi.csv", IntestazioneRighe,
            "SIC24_1.1.1.1;1;Manodopera;2;Operaio specializzato;h;0.021788;0;35;MO24_S - Tariffa;;MO24_S",
            "SIC24_1.1.1.1;2;Manodopera;2;Operaio comune;h;0.021788;0;22;MO24_C - Tariffa;;MO24_C",
            $"SIC24_1.1.1.1;3;Attrezzature;3;Escavatore cingolato 22 t;h;0.02;0;{prezzoEscavatore};AT24_N3 - Noli a freddo;;AT24_N3",
            "SIC24_9.1.1;2;Manodopera;2;Operaio qualificato;h;0.5;0;30;MO24_Q;;MO24_Q",
            $"SIC24_9.1.1;1;Materiali;1;Malta premiscelata;m³;0.022;0;{prezzoMalta};PR24_01M0008;;PR24_01M0008",
            "SIC24_9.1.10;1;Materiali;1;Massetto premiscelato;kg;20;0;0.25;MC24_001;;MC24_001"),
    ];

    [Fact]
    public async Task Prima_importazione_crea_le_analisi_nello_stato_del_file()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var import = amb.Get<ImportazioneAnalisiService>();
        var analisi = amb.Get<AnalisiService>();

        var anteprima = await import.AnteprimaAsync(Tracciato());
        Assert.Equal(3, anteprima.Conta(EsitoAnalisiDaFile.Nuova));
        Assert.Equal(1, anteprima.InBozza);
        Assert.Empty(anteprima.Avvisi);
        Assert.Empty(await analisi.ElencoAsync());                       // l'anteprima non salva nulla

        var esito = await import.ConfermaAsync(Tracciato());
        Assert.Equal(new EsitoImportazioneAnalisi(3, 0, 0, 0, 0), esito);

        var elenco = await analisi.ElencoAsync();
        Assert.Equal(["SIC24_1.1.1.1", "SIC24_9.1.1", "SIC24_9.1.10"], elenco.Select(a => a.Codice));   // ordine naturale dei codici
        var scavo = elenco[0];
        Assert.Equal("mc", scavo.Unita);
        Assert.Equal(StatoAnalisi.Confermata, scavo.Stato);
        Assert.Equal(0.021788m * 35 + 0.021788m * 22 + 0.02m * 86.26m, scavo.CostoUnitario);
        Assert.Equal(StatoAnalisi.Bozza, elenco[2].Stato);

        // Le risorse seguono la colonna Ordine; unità normalizzate, fonte con il codice della risorsa.
        var intonaco = (await analisi.OttieniAsync(elenco[1].Id))!;
        Assert.Equal(["Malta premiscelata", "Operaio qualificato"], intonaco.Righe.Select(r => r.Descrizione));
        Assert.Equal("mc", intonaco.Righe[0].Unita);
        Assert.Equal("PR24_01M0008", intonaco.Righe[0].Fonte);
        Assert.Equal("Anomalia media; da verificare", (await analisi.OttieniAsync(elenco[2].Id))!.Note);

        // Le confermate alimentano le voci d'offerta; quelle in bozza no.
        var commessa = (await amb.Get<CommesseService>().CreaAsync("Commessa SIC24")).Id;
        Assert.Equal(2, (await analisi.RisorseDaApplicareAsync(intonaco.Id, commessa)).Count);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => analisi.RisorseDaApplicareAsync(elenco[2].Id, commessa));

        // Una seconda importazione dello stesso file non ha nulla da salvare.
        var ancora = await import.AnteprimaAsync(Tracciato());
        Assert.Equal(3, ancora.Conta(EsitoAnalisiDaFile.Invariata));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => import.ConfermaAsync(Tracciato()));
    }

    [Fact]
    public async Task Le_analisi_modificate_in_Corico_si_conservano()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var import = amb.Get<ImportazioneAnalisiService>();
        var analisi = amb.Get<AnalisiService>();
        await import.ConfermaAsync(Tracciato());
        var elenco = await analisi.ElencoAsync();

        // A mano: nuovo prezzo della manodopera dell'intonaco e conferma del massetto dopo la verifica.
        var intonaco = (await analisi.OttieniAsync(elenco[1].Id))!;
        intonaco.Righe[1].Prezzo = 32;
        await analisi.SalvaAsync(intonaco);
        await analisi.CambiaStatoAsync(elenco[2].Id, StatoAnalisi.Confermata);

        // Il nuovo file cambia il prezzo della malta (intonaco) e del nolo (scavo).
        var anteprima = await import.AnteprimaAsync(Tracciato(prezzoMalta: 160, prezzoEscavatore: 90));
        var perCodice = anteprima.Analisi.ToDictionary(a => a.Codice!);
        Assert.Equal(EsitoAnalisiDaFile.Aggiornata, perCodice["SIC24_1.1.1.1"].Esito);
        Assert.Contains("costo unitario", perCodice["SIC24_1.1.1.1"].Dettaglio);
        Assert.Equal(EsitoAnalisiDaFile.ModificataInCorico, perCodice["SIC24_9.1.1"].Esito);
        Assert.Equal(EsitoAnalisiDaFile.ModificataInCorico, perCodice["SIC24_9.1.10"].Esito);   // anche il solo cambio di stato

        var esito = await import.ConfermaAsync(Tracciato(prezzoMalta: 160, prezzoEscavatore: 90));
        Assert.Equal(new EsitoImportazioneAnalisi(0, 1, 0, 2, 0), esito);

        var dopo = await analisi.ElencoAsync();
        Assert.Equal(0.021788m * 35 + 0.021788m * 22 + 0.02m * 90, dopo[0].CostoUnitario);
        Assert.Equal(0.022m * 150 + 0.5m * 32, dopo[1].CostoUnitario);                       // modifica a mano conservata
        Assert.Equal(StatoAnalisi.Confermata, dopo[2].Stato);
        Assert.Equal(elenco[0].Id, dopo[0].Id);                                               // aggiornata, non ricreata

        // L'analisi aggiornata dal file, se non toccata, si aggiorna ancora alla prossima importazione.
        var terza = await import.AnteprimaAsync(Tracciato(prezzoMalta: 160, prezzoEscavatore: 95));
        Assert.Equal(EsitoAnalisiDaFile.Aggiornata, terza.Analisi.Single(a => a.Codice == "SIC24_1.1.1.1").Esito);
    }

    [Fact]
    public async Task Analisi_creata_a_mano_con_lo_stesso_codice_si_conserva()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var analisi = amb.Get<AnalisiService>();
        var a = new AnalisiAziendale { Id = Guid.Empty, Codice = "SIC24_9.1.1", Descrizione = "Intonaco a modo mio", Unita = "mq" };
        a.Righe.Add(new RigaAnalisiAziendale { Categoria = CategoriaRisorsa.Manodopera, Descrizione = "Operaio", Unita = "h", Fabbisogno = 1, Prezzo = 30 });
        await analisi.SalvaAsync(a);

        var esito = await amb.Get<ImportazioneAnalisiService>().ConfermaAsync(Tracciato());
        Assert.Equal(new EsitoImportazioneAnalisi(2, 0, 0, 1, 0), esito);
        Assert.Equal("Intonaco a modo mio", (await analisi.ElencoAsync()).Single(x => x.Codice == "SIC24_9.1.1").Descrizione);
    }

    [Fact]
    public async Task Errori_del_file_segnalati_per_analisi()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var import = amb.Get<ImportazioneAnalisiService>();
        List<FileCaricato> file =
        [
            Csv("Analisi.csv", Intestazione,
                "A1;Buona;mq;Confermata;;;",
                "A2;Prezzo negativo;mq;Confermata;;;",
                "A3;;mq;Bozza;;;",
                "A4;Categoria sconosciuta;mq;Bozza;;;",
                "A1;Ripetuta;mq;Bozza;;;",
                "A5;Confermata senza risorse;mq;Confermata;;;",
                "A6;Stato strano;mq;Approvata;;;"),
            Csv("RigheAnalisi.csv", IntestazioneRighe,
                "A1;1;Materiali;1;Malta;mc;0,02;0;150,5;;;",
                "A2;1;Materiali;1;Malta;mc;0.02;0;-1;;;",
                "A3;1;Materiali;1;Malta;mc;0.02;0;1;;;",
                "A4;1;Varie;;Malta;mc;0.02;0;1;;;",
                "A6;1;Materiali;1;Malta;mc;0.02;0;1;;;",
                "ZZ;1;Materiali;1;Malta;mc;0.02;0;1;;;"),
        ];
        var anteprima = await import.AnteprimaAsync(file);
        foreach (var a in anteprima.Analisi) output.WriteLine($"{a.Codice} {a.Esito}: {a.Dettaglio}");
        var buona = anteprima.Analisi.Single(a => a.Codice == "A1" && a.Descrizione == "Buona");
        Assert.Equal(EsitoAnalisiDaFile.Nuova, buona.Esito);
        Assert.Equal(0.02m * 150.5m, buona.CostoUnitario);                                  // anche con la virgola decimale
        Assert.Equal(6, anteprima.Conta(EsitoAnalisiDaFile.Errata));
        Assert.Contains("negativo", anteprima.Analisi.Single(a => a.Codice == "A2").Dettaglio);
        Assert.Contains("Descrizione mancante", anteprima.Analisi.Single(a => a.Codice == "A3").Dettaglio);
        Assert.Contains("Categoria", anteprima.Analisi.Single(a => a.Codice == "A4").Dettaglio);
        Assert.Contains("ripetuto", anteprima.Analisi.Single(a => a.Descrizione == "Ripetuta").Dettaglio);
        Assert.Contains("senza risorse", anteprima.Analisi.Single(a => a.Codice == "A5").Dettaglio);
        Assert.Contains("Stato", anteprima.Analisi.Single(a => a.Codice == "A6").Dettaglio);
        Assert.Contains(anteprima.Avvisi, a => a.Contains("1 righe"));

        Assert.Equal(new EsitoImportazioneAnalisi(1, 0, 0, 0, 6), await import.ConfermaAsync(file));
    }

    [Fact]
    public async Task File_senza_tabella_delle_risorse()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var ex = await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
            amb.Get<ImportazioneAnalisiService>().AnteprimaAsync([Csv("Analisi.csv", Intestazione, "A1;Buona;mq;Confermata;;;")]));
        Assert.Contains("RigheAnalisi", ex.Message);
    }

    [Theory]
    [InlineData("SIC24_1.1.2", "SIC24_1.1.10")]
    [InlineData("SIC24_1.9.1", "SIC24_10.1.1")]
    [InlineData("AN.01", "an.02")]
    public void Ordine_naturale_dei_codici(string prima, string dopo)
    {
        Assert.True(OrdineCodici.Istanza.Compare(prima, dopo) < 0);
        Assert.True(OrdineCodici.Istanza.Compare(dopo, prima) > 0);
    }

    /// <summary>
    /// Collaudo con il tracciato reale delle Analisi costo SIC24 (Excel e CSV): 6.542 analisi, 175 in Bozza.
    /// Il file non è nel repository: il test si esegue dove è presente.
    /// </summary>
    [Fact]
    public async Task Collaudo_tracciato_SIC24()
    {
        var cartella = Environment.GetEnvironmentVariable("CORICO_SIC24") ?? "/mnt/project-files/analisi-costo/corico";
        var xlsx = Path.Combine(cartella, "SIC24_Import_CORICO.xlsx");
        if (!File.Exists(xlsx)) return;
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var import = amb.Get<ImportazioneAnalisiService>();
        var excel = new List<FileCaricato> { new(Path.GetFileName(xlsx), await File.ReadAllBytesAsync(xlsx)) };

        var orologio = System.Diagnostics.Stopwatch.StartNew();
        var anteprima = await import.AnteprimaAsync(excel);
        output.WriteLine($"Anteprima in {orologio.ElapsedMilliseconds} ms");
        foreach (var a in anteprima.Analisi.Where(a => a.Esito == EsitoAnalisiDaFile.Errata).Take(20)) output.WriteLine($"{a.Codice}: {a.Dettaglio}");
        foreach (var a in anteprima.Avvisi) output.WriteLine("Avviso: " + a);
        Assert.Equal(6_542, anteprima.Conta(EsitoAnalisiDaFile.Nuova));
        Assert.Equal(0, anteprima.Conta(EsitoAnalisiDaFile.Errata));
        Assert.Equal(175, anteprima.InBozza);
        Assert.Equal(29_959, anteprima.Analisi.Sum(a => a.Risorse));
        Assert.Equal(46, anteprima.VociEscluse);

        // Costo unitario ricalcolato con la formula di Corico uguale a quello del tracciato (al centesimo).
        var attesi = File.ReadLines(Path.Combine(cartella, "SIC24_CORICO_Analisi.csv")).Skip(1)
            .Select(r => r.Split(';'))
            .ToDictionary(c => c[0], c => decimal.Parse(c[^3], System.Globalization.CultureInfo.InvariantCulture));
        Assert.All(anteprima.Analisi, a => Assert.True(Math.Abs(a.CostoUnitario - attesi[a.Codice!]) < 0.01m, a.Codice));

        orologio.Restart();
        var esito = await import.ConfermaAsync(excel);
        output.WriteLine($"Importazione in {orologio.ElapsedMilliseconds} ms");
        Assert.Equal(6_542, esito.Nuove);

        // I CSV dello stesso tracciato corrispondono all'Excel già importato.
        var csv = new List<FileCaricato>();
        foreach (var nome in new[] { "SIC24_CORICO_Analisi.csv", "SIC24_CORICO_RigheAnalisi.csv", "SIC24_CORICO_Escluse.csv" })
            csv.Add(new FileCaricato(nome, await File.ReadAllBytesAsync(Path.Combine(cartella, nome))));
        orologio.Restart();
        var daCsv = await import.AnteprimaAsync(csv);
        output.WriteLine($"Anteprima dei CSV in {orologio.ElapsedMilliseconds} ms");
        Assert.Equal(6_542, daCsv.Conta(EsitoAnalisiDaFile.Invariata));

        var ricerca = await amb.Get<AnalisiService>().CercaAsync(null, false, 200);
        Assert.Equal(6_542, ricerca.Totale);
        Assert.Equal(200, ricerca.Analisi.Count);
        Assert.Equal("SIC24_1.1.1.1", ricerca.Analisi[0].Codice);
    }
}
