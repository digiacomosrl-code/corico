using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;
using Xunit.Abstractions;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>
/// Collaudo del Modulo 1 (sez. 23): la commessa di Caltagirone (cod. 214), ricostruita dal file Excel aziendale,
/// deve dare per ogni pacchetto del tabellone HOME gli stessi ricavi netti e costi del file.
/// Il file contiene dati aziendali e non è nel repository: il test si esegue dove il file è presente.
/// </summary>
public class CollaudoCaltagironeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Ricostruzione_della_commessa_214_dal_file_Excel()
    {
        if (LettoreFogliCrTests.Campione("1.Surly Caltagirone.xlsx") is not { } percorso) return;
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var commessa = await amb.Get<CommesseService>().CreaAsync("Restauro CDD Caltagirone");
        var import = amb.Get<ImportazionePianificazioneService>();
        var contenuto = await File.ReadAllBytesAsync(percorso);

        var anteprima = await import.AnteprimaAsync(commessa.Id, new MemoryStream(contenuto));
        Assert.Equal(21.5m, anteprima.RibassoProposto);
        foreach (var s in anteprima.Segnalazioni) output.WriteLine($"[{s.Gravita}] {s.Testo}");

        var esito = await import.ConfermaAsync(commessa.Id, contenuto, Path.GetFileName(percorso), anteprima);
        foreach (var v in esito.Verifiche)
            output.WriteLine($"{(v.Concorde ? "OK " : "DIFF")} {v.Pacchetto} / {v.Foglio}{(v.Scelto ? " *" : "")}: ricavo {v.RicavoFoglio:N2} → {v.RicavoCorico:N2}, costo {v.CostoFoglio:N2} → {v.CostoCorico:N2}");
        foreach (var a in esito.Avvisi) output.WriteLine("Avviso: " + a);

        var tabellone = await amb.Get<PacchettiService>().TabelloneAsync(commessa.Id);
        RigaTabellone Riga(string nome) => tabellone.Righe.Single(r => r.Descrizione == nome);

        // Valori del foglio HOME (Ponteggio: ricavo da progetto, senza l'integrazione C.M. rimandata — decisione 3).
        Assert.Equal(94_278.65m, Math.Round(Riga("Ponteggio").RicavoNetto, 2));
        Assert.Equal(75_415.00m, Riga("Ponteggio").Costo);
        Assert.Equal("Armilli Costruzioni srls", Riga("Ponteggio").Fornitore);
        Assert.Equal(27_867.67m, Math.Round(Riga("Vetrate interne").RicavoNetto, 2));
        Assert.Equal(15_695.00m, Riga("Vetrate interne").Costo);
        Assert.Equal(23_633.84m, Math.Round(Riga("Piattaforma elevatrice").RicavoNetto, 2));
        Assert.Equal(16_500.00m, Riga("Piattaforma elevatrice").Costo);
        Assert.Equal(7_133.84m, Math.Round(Riga("Piattaforma elevatrice").Mdc!.Value, 2));

        // Tutti i fogli: ricavo e costo ricalcolati da Corico identici a quelli dell'Excel.
        Assert.All(esito.Verifiche, v => Assert.True(v.Concorde, $"{v.Foglio}: {v.RicavoFoglio} / {v.RicavoCorico}, {v.CostoFoglio} / {v.CostoCorico}"));

        var ponteggio = await amb.Get<PacchettiService>().DettaglioAsync(commessa.Id, Riga("Ponteggio").Id);
        Assert.Equal(3, ponteggio!.Offerte.Count);
        Assert.Equal(StatoCosto.Provvisorio, ponteggio.Pacchetto.StatoCosto);
        Assert.Contains(ponteggio.Note, n => n.Iniziali == "DGS" && n.Data == new DateOnly(2024, 11, 25));
        Assert.All(ponteggio.Voci, v => Assert.Equal(100m, v.PercentualeNonSoggetta));
    }
}
