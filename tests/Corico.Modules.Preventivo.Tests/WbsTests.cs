using System.Text;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>WBS della commessa (versione 1.2.1): struttura, assegnazione, importazione, proposta, pacchetti e ordini.</summary>
public class WbsTests
{
    private static readonly string Esempi = Environment.GetEnvironmentVariable("CORICO_ESEMPI") ?? "/mnt/project-files/Esempi";

    /// <summary>Il computo di prova con la struttura WBS di PriMus e il codice 2.1 sulla prima voce (2.1 non è nella struttura del file).</summary>
    private static byte[] XpweConWbs => Encoding.UTF8.GetBytes(XpweDiProva.Testo.Replace("\\u0003", "\u0003")
        .Replace("</PweDGCapitoliCategorie>", """
            </PweDGCapitoliCategorie>
            <PweDGWBS>
              <DGWBSItem ID="2"><TITOLO>WBS</TITOLO><CODICE/></DGWBSItem>
              <DGWBSItem ID="3"><TITOLO>&lt;NON assegnata&gt;</TITOLO><CODICE/></DGWBSItem>
              <DGWBSItem ID="4"><TITOLO>OPERE EDILI</TITOLO><CODICE>2</CODICE></DGWBSItem>
            </PweDGWBS>
            """)
        .Replace("<VCItem ID=\"1\"><IDEP>10</IDEP>", "<VCItem ID=\"1\"><CodiceWBS>2.1</CodiceWBS><IDEP>10</IDEP>"));

    [Fact]
    public void Codici_ordine_naturale_gerarchia_e_normalizzazione()
    {
        string[] codici = ["2.10", "10", "2", "2.9", "1.4.11", "1.4.9", "0"];
        Assert.Equal(["0", "1.4.9", "1.4.11", "2", "2.9", "2.10", "10"], codici.Order(CodiciWbs.Ordine).ToArray());
        Assert.True(CodiciWbs.Rientra("2.3.1", "2"));
        Assert.True(CodiciWbs.Rientra("2.3", "2.3"));
        Assert.False(CodiciWbs.Rientra("2.30", "2.3"));
        Assert.False(CodiciWbs.Rientra("12.3", "2"));
        Assert.Equal("6.2", CodiciWbs.Padre("6.2.1"));
        Assert.Equal(3, CodiciWbs.Livello("6.2.1"));
        Assert.Equal("2.3", CodiciWbs.Normalizza(" 2,3. "));
        Assert.Null(CodiciWbs.Normalizza("  "));
    }

    [Fact]
    public void Lettore_xpwe_legge_struttura_e_codici_wbs()
    {
        var letto = LettoreXpwe.Leggi(new MemoryStream(XpweConWbs));
        Assert.Equal([new WbsLetta("2", "OPERE EDILI")], letto.Wbs);
        Assert.Equal("2.1", letto.Voci[0].CodiceWbs);
        Assert.Null(letto.Voci[1].CodiceWbs);
    }

    [Fact]
    public void Campione_biancavilla_porta_la_wbs_di_primus()
    {
        var percorso = Path.Combine(Esempi, "BIANCAVILLA.XPWE");
        if (!File.Exists(percorso)) return;
        using var f = File.OpenRead(percorso);
        var letto = LettoreXpwe.Leggi(f);
        Assert.Equal(84, letto.Voci.Count(v => v.CodiceWbs is not null));
        Assert.Contains(new WbsLetta("1.1", "Lavori Edizeta S.p.A."), letto.Wbs);
    }

    [Fact]
    public async Task Importazione_biancavilla_completa_i_livelli_superiori()
    {
        var percorso = Path.Combine(Esempi, "BIANCAVILLA.XPWE");
        if (!File.Exists(percorso)) return;
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Biancavilla")).Id;
        var esito = await amb.Get<ImportazioneComputoService>().ConfermaAsync(id, await File.ReadAllBytesAsync(percorso), "BIANCAVILLA.XPWE", null);
        Assert.Equal(84, esito.VociConWbsDalFile);
        var struttura = await amb.Get<WbsService>().ElencoAsync(id);
        var l14 = struttura.Single(e => e.Codice == "1.4");      // non è nel file: aggiunto perché ci sono 1.4.3, 1.4.4…
        Assert.StartsWith("WBS 1.4 (dal computo", l14.Descrizione);
        Assert.Equal(struttura.Where(e => e.Codice.StartsWith("1.4.")).Sum(e => e.VociDirette), l14.Voci);
        Assert.Equal("Lavori Nicolosi", struttura.Single(e => e.Codice == "1.2").Descrizione);
    }

    [Fact]
    public void Excel_della_wbs_aziendale_con_codice_ripetuto_segnalato()
    {
        var percorso = Path.Combine(Esempi, "2.Fargo Caltagirone.xlsx");
        if (!File.Exists(percorso)) return;
        using var f = File.OpenRead(percorso);
        var a = WbsService.AnteprimaExcel(f);
        Assert.Contains(new ElementoWbsDati("2.3", "Pavimenti"), a.Elementi);
        Assert.Contains(new ElementoWbsDati("6.2.1", "Distribuzione"), a.Elementi);
        Assert.Contains(new ElementoWbsDati("0", "INCANTIERAMENTO"), a.Elementi);
        Assert.Contains(a.Segnalazioni, s => s.Contains("ONERI PER LA SICUREZZA"));   // anche questa ha codice 0 nel file
        Assert.DoesNotContain(a.Elementi, e => e.Codice == "WBC");
    }

    [Fact]
    public async Task Importazione_crea_la_struttura_e_riporta_la_wbs_al_computo_successivo()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        var import = amb.Get<ImportazioneComputoService>();
        var wbs = amb.Get<WbsService>();

        var esito = await import.ConfermaAsync(id, XpweConWbs, "prova.xpwe", null);
        Assert.Equal(1, esito.VociConWbsDalFile);
        var struttura = await wbs.ElencoAsync(id);
        Assert.Equal(["2", "2.1"], struttura.Select(e => e.Codice));
        Assert.Equal("OPERE EDILI", struttura[0].Descrizione);
        Assert.StartsWith("WBS 2.1 (dal computo", struttura[1].Descrizione);
        Assert.Equal(1, struttura[0].Voci);                // comprende il sottolivello 2.1
        Assert.Equal(0, struttura[0].VociDirette);

        // Assegnazione a mano della voce del ponteggio, poi nuovo computo senza WBS: la WBS si riporta.
        await wbs.SalvaAsync(id, Guid.Empty, 0, "9", "Sicurezza");
        var voci = await amb.Get<PreventivoService>().VociAsync(id);
        await wbs.AssegnaAsync(id, [voci.Single(v => v.Tariffa == "S.01").Id], "9");
        var secondo = await import.ConfermaAsync(id, XpweDiProva.Contenuto, "revisione.xpwe", null);
        Assert.Equal(0, secondo.VociConWbsDalFile);
        Assert.Equal(2, secondo.VociConWbsRiportata);
        voci = await amb.Get<PreventivoService>().VociAsync(id);
        Assert.Equal("2.1", voci.Single(v => v.Tariffa == "E.01").CodiceWbs);
        Assert.Equal("9", voci.Single(v => v.Tariffa == "S.01").CodiceWbs);
    }

    [Fact]
    public async Task Struttura_rinomina_eliminazione_e_assegnazione_controllate()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        await amb.Get<ImportazioneComputoService>().ConfermaAsync(id, XpweConWbs, "prova.xpwe", null);
        var wbs = amb.Get<WbsService>();
        var prv = amb.Get<PreventivoService>();

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => wbs.SalvaAsync(id, Guid.Empty, 0, "2", "Doppione"));
        var voci = await prv.VociAsync(id);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => wbs.AssegnaAsync(id, [voci[1].Id], "7.7"));

        // Il primo livello ha un sottolivello e il sottolivello ha una voce: non si eliminano.
        var struttura = await wbs.ElencoAsync(id);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => wbs.EliminaAsync(id, struttura[0].Id));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => wbs.EliminaAsync(id, struttura[1].Id));

        // Rinominando «2» in «3» cambiano anche il sottolivello e la voce assegnata.
        await wbs.SalvaAsync(id, struttura[0].Id, struttura[0].Versione, "3", "Opere edili");
        Assert.Equal(["3", "3.1"], (await wbs.ElencoAsync(id)).Select(e => e.Codice));
        Assert.Equal("3.1", (await prv.VociAsync(id)).Single(v => v.Tariffa == "E.01").CodiceWbs);

        // Anche dalla scheda della voce, solo con codici della struttura.
        var dati = new DatiVoce { Id = voci[1].Id, Versione = voci[1].Versione, Tariffa = voci[1].Tariffa, Descrizione = voci[1].Descrizione,
            Unita = voci[1].Unita, Quantita = voci[1].Quantita, Prezzo = voci[1].Prezzo, PercentualeNonSoggetta = voci[1].PercentualeNonSoggetta, CodiceWbs = "8" };
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => prv.SalvaVoceAsync(id, dati));
        dati.CodiceWbs = "3";
        await prv.SalvaVoceAsync(id, dati);
        Assert.Equal("3", (await prv.VociAsync(id)).Single(v => v.Id == voci[1].Id).CodiceWbs);

        await wbs.AssegnaAsync(id, [voci[1].Id], null);
        Assert.Null((await prv.VociAsync(id)).Single(v => v.Id == voci[1].Id).CodiceWbs);
    }

    [Fact]
    public async Task Proposta_dalle_categorie_senza_wbs_poi_per_parole_e_tariffa()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        await amb.Get<ImportazioneComputoService>().ConfermaAsync(id, XpweDiProva.Contenuto, "prova.xpwe", null);
        var wbs = amb.Get<WbsService>();

        // Nessuna WBS: struttura dalle supercategorie del computo (OPERE EDILI, SICUREZZA) e assegnazioni per categoria.
        var p = await wbs.ProponiAsync(id);
        Assert.Equal([new ElementoWbsDati("1", "OPERE EDILI"), new ElementoWbsDati("2", "SICUREZZA")], p.NuoviElementi);
        Assert.Equal(3, p.Assegnazioni.Count);
        Assert.All(p.Assegnazioni, a => Assert.Equal(FontePropostaWbs.Categoria, a.Fonte));
        Assert.Empty(await wbs.ElencoAsync(id));          // la proposta non salva nulla

        // Confermata solo in parte: la voce esclusa resta senza WBS.
        var esclusa = p.Assegnazioni[2].VoceId;
        Assert.Equal(2, await wbs.ApplicaPropostaAsync(id, p.NuoviElementi, p.Assegnazioni.Where(a => a.VoceId != esclusa).ToList()));
        Assert.Equal(2, (await wbs.ElencoAsync(id)).Count);

        // Con una WBS aziendale: parole in comune tra descrizioni.
        await using var amb2 = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id2 = (await amb2.Get<CommesseService>().CreaAsync("Altra commessa")).Id;
        await amb2.Get<ImportazioneComputoService>().ConfermaAsync(id2, XpweDiProva.Contenuto, "prova.xpwe", null);
        var wbs2 = amb2.Get<WbsService>();
        await wbs2.CaricaAsync(id2, [new("2", "OPERE EDILI"), new("2.5", "Intonaci"), new("0", "Oneri per la sicurezza e ponteggi")], "prova");
        var p2 = await wbs2.ProponiAsync(id2);
        Assert.Empty(p2.NuoviElementi);
        var voci = await amb2.Get<PreventivoService>().VociAsync(id2);
        var intonaco = p2.Assegnazioni.Single(a => a.VoceId == voci.Single(v => v.Tariffa == "E.01").Id);
        Assert.Equal("2.5", intonaco.Codice);             // «Intonaco civile» ~ «Intonaci», più profondo di «Opere edili»
        Assert.Equal(FontePropostaWbs.ParoleChiave, intonaco.Fonte);
        Assert.Equal("0", p2.Assegnazioni.Single(a => a.VoceId == voci.Single(v => v.Tariffa == "S.01").Id).Codice);
    }

    [Fact]
    public async Task Proposta_stessa_tariffa()
    {
        var voce = (Guid Id, string Tariffa, string? Wbs) => new VocePerWbs(Id, 1, Tariffa, "Voce", null, null, Wbs, 0);
        var a = Guid.NewGuid();
        var p = await new PropostaWbsARegole().ProponiAsync(new ContestoPropostaWbs(Guid.NewGuid(), [new("4", "Opere in ferro")],
            [voce(Guid.NewGuid(), "F.01", "4"), voce(a, "F.01", null)]));
        Assert.Equal(new AssegnazioneWbsProposta(a, "4", FontePropostaWbs.StessaTariffa, "Stessa tariffa F.01 già in WBS 4"), p.Assegnazioni.Single());
    }

    [Fact]
    public async Task Pacchetti_e_ordini_riportano_la_wbs_delle_voci_anche_a_budget_approvato()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var cliente = (await amb.Get<ClientiService>().SalvaAsync(new DatiCliente { Denominazione = "Comune di Caltagirone" })).Id;
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova", cliente)).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "A", Descrizione = "Porta a un'anta", Unita = "cad", Quantita = 10, Prezzo = 600 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "B", Descrizione = "Porta REI", Unita = "cad", Quantita = 2, Prezzo = 900 });
        var voci = await prv.VociAsync(id);
        var wbs = amb.Get<WbsService>();
        await wbs.CaricaAsync(id, [new("5", "INFISSI"), new("5.1", "Infissi interni"), new("5.3", "Porte REI")], "prova");
        await wbs.AssegnaAsync(id, [voci[0].Id], "5.1");
        await wbs.AssegnaAsync(id, [voci[1].Id], "5.3");

        var pacchetti = amb.Get<PacchettiService>();
        var pacchetto = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte", Tipo = TipoAffidamento.Fornitura });
        await pacchetti.AssegnaVociAsync(id, pacchetto, [voci[0].Id, voci[1].Id]);
        var offerta = await pacchetti.NuovaOffertaAsync(id, pacchetto);
        offerta.NomeFornitore = "Padilla";
        offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id).Fornitura = 400;
        offerta.Voci.Single(v => v.VoceComputoId == voci[1].Id).Fornitura = 500;
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Trasporto", Importo = 150 });
        await pacchetti.SalvaOffertaAsync(id, offerta);

        var riga = (await pacchetti.TabelloneAsync(id)).Righe.Single();
        Assert.Equal(["5.1", "5.3"], riga.Wbs);
        Assert.Equal(0, riga.VociSenzaWbs);

        var ordini = amb.Get<OrdiniService>();
        var bozza = (await ordini.OttieniAsync(id, await ordini.PreparaDaPacchettoAsync(id, pacchetto)))!;
        Assert.Equal("5.1", bozza.Righe.Single(r => r.Descrizione == "Porta a un'anta").Wbs);
        Assert.Equal("5.3", bozza.Righe.Single(r => r.Descrizione == "Porta REI").Wbs);
        Assert.Equal("5.1, 5.3", bozza.Righe.Single(r => r.Descrizione == "Trasporto").Wbs);   // voce a corpo: WBS del pacchetto
        var pdf = await amb.Get<EsportazioniService>().OrdinePdfAsync(id, bozza.Id);
        Assert.True(pdf.Contenuto.Length > 1000);

        // Riga libera aggiunta nella bozza: la WBS si scrive a mano.
        bozza.Righe.Add(new RigaOrdine { Id = Guid.Empty, Ordine = 9, Descrizione = "Imballo speciale", Quantita = 1, Prezzo = 50, Wbs = "5.1" });
        await ordini.SalvaBozzaAsync(id, bozza);
        Assert.Equal("5.1", (await ordini.OttieniAsync(id, bozza.Id))!.Righe.Single(r => r.Descrizione == "Imballo speciale").Wbs);

        // Budget 0 approvato: i prezzi sono bloccati, la WBS no.
        var budget = amb.Get<BudgetService>();
        var admin = new IdentitaUtente(Guid.NewGuid(), "salvatore", "Salvatore", RuoloUtente.Amministratore);
        await ordini.EliminaBozzaAsync(id, bozza.Id);
        using (UtenteCorrente.Imposta(admin))
        {
            var proposta = await budget.ProponiAsync(id, null);
            await budget.ApprovaAsync(id, proposta.Id);
        }
        await wbs.AssegnaAsync(id, [voci[1].Id], "5.1");
        Assert.Equal(["5.1"], (await pacchetti.TabelloneAsync(id)).Righe.Single().Wbs);
    }
}
