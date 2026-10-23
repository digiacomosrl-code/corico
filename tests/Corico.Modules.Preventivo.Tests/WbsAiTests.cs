using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>Proposta WBS dall'AI: contenuto inviato, lettura e controllo della risposta, applicazione in sostituzione.</summary>
public class WbsAiTests
{
    private static VocePerWbs Voce(int ordine, string tariffa, string descrizione, string? wbs = null) =>
        new(Guid.NewGuid(), ordine, tariffa, descrizione, "OPERE EDILI", "Finiture", wbs, 100m * ordine);

    [Fact]
    public void Contenuto_numera_le_voci_nell_ordine_del_computo_e_accorcia_le_descrizioni()
    {
        var lunga = "Intonaco | civile " + new string('x', 500);
        var contesto = new ContestoPropostaWbs(Guid.NewGuid(), [], [Voce(2, "E.02", lunga), Voce(1, "E.01", "Scavo\na sezione")]);
        var righe = WbsAi.Contenuto(contesto).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("1 | E.01 | OPERE EDILI > Finiture | Scavo a sezione | 100.00", righe[2]);
        Assert.StartsWith("2 | E.02 | OPERE EDILI > Finiture | Intonaco / civile", righe[3]);
        Assert.Contains("…", righe[3]);
        Assert.True(righe[3].Length < WbsAi.LunghezzaDescrizione + 80);
    }

    [Fact]
    public void Risposta_controllata_ordine_cronologico_livelli_mancanti_e_voci_scoperte()
    {
        var voci = new[] { Voce(1, "A", "Pavimento"), Voce(2, "B", "Scavo"), Voce(3, "C", "Massetto"), Voce(4, "D", "Ponteggio") };
        var contesto = new ContestoPropostaWbs(Guid.NewGuid(), [], voci);
        const string risposta = """
            Ecco la WBS:
            ```json
            { "tipologia": "Ristrutturazione", "criteri": "Prima il cantiere, poi scavi e finiture.",
              "elementi": [
                { "codice": "1", "descrizione": "Allestimento cantiere", "motivo": "Serve prima di tutto", "voci": [4] },
                { "codice": "2.1", "descrizione": "Scavi", "motivo": "Dopo l'allestimento", "voci": [2, 9] },
                { "codice": "3.2", "descrizione": "Pavimenti", "motivo": "Sul massetto", "voci": [1, 2] },
                { "codice": "3.1", "descrizione": "Massetti", "motivo": "Prima dei pavimenti", "voci": [] },
                { "codice": "3.1", "descrizione": "Doppione", "motivo": "", "voci": [3] },
                { "codice": "A.1", "descrizione": "Non numerico", "motivo": "", "voci": [] }
              ] }
            ```
            """;
        var p = WbsAi.Interpreta(risposta, contesto);

        Assert.Equal(["1", "2", "2.1", "3", "3.1", "3.2"], p.NuoviElementi.Select(e => e.Codice));
        Assert.Equal("WBS 2 (da descrivere)", p.NuoviElementi.Single(e => e.Codice == "2").Descrizione);
        Assert.All(p.Assegnazioni, a => Assert.Equal(FontePropostaWbs.Ai, a.Fonte));
        Assert.Equal("1", p.Assegnazioni.Single(a => a.VoceId == voci[3].Id).Codice);
        Assert.Equal("2.1", p.Assegnazioni.Single(a => a.VoceId == voci[1].Id).Codice);   // la seconda indicazione è scartata
        Assert.Equal("Sul massetto", p.Assegnazioni.Single(a => a.VoceId == voci[0].Id).Motivo);
        Assert.DoesNotContain(p.Assegnazioni, a => a.VoceId == voci[2].Id);                // stava solo nel doppione
        Assert.Contains(p.Note, n => n.Contains("Ristrutturazione"));
        Assert.Contains(p.Note, n => n.Contains("3.1") && n.Contains("A.1"));
        Assert.Contains(p.Note, n => n.Contains("inesistenti") && n.Contains('9'));
        Assert.Contains(p.Note, n => n.Contains("più elementi") && n.Contains('2'));
        Assert.Contains(p.Note, n => n.StartsWith("1 voci non associate") && n.Contains("n. 3"));
    }

    [Fact]
    public void Risposta_illeggibile_nessuna_proposta()
    {
        var contesto = new ContestoPropostaWbs(Guid.NewGuid(), [], [Voce(1, "A", "Scavo")]);
        foreach (var testo in new[] { null, "", "non è JSON", """{ "elementi": [] }""" })
        {
            var p = WbsAi.Interpreta(testo, contesto);
            Assert.Empty(p.NuoviElementi);
            Assert.Empty(p.Assegnazioni);
            Assert.Single(p.Note);
        }
    }

    [Fact]
    public async Task Proposta_ai_applicata_in_sostituzione_della_wbs_esistente()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        await amb.Get<ImportazioneComputoService>().ConfermaAsync(id, XpweDiProva.Contenuto, "prova.xpwe", null);
        var wbs = amb.Get<WbsService>();
        await wbs.CaricaAsync(id, [new("1", "Vecchia"), new("1.1", "Vecchia figlia")], "prova");
        var contesto = await wbs.ContestoAsync(id);
        await wbs.AssegnaAsync(id, [contesto.Voci[0].Id], "1.1");

        // L'AI rinumera: 1 = sicurezza (prima fase), 2 = opere edili, 2.1 = intonaci.
        var risposta = """
            { "tipologia": "Manutenzione", "criteri": "Sicurezza, poi opere edili.", "elementi": [
              { "codice": "1", "descrizione": "Sicurezza e cantiere", "motivo": "Prima fase", "voci": [3] },
              { "codice": "2", "descrizione": "Opere edili", "motivo": "Dopo il cantiere", "voci": [1] },
              { "codice": "2.1", "descrizione": "Intonaci", "motivo": "Finitura", "voci": [2] } ] }
            """;
        var p = WbsAi.Interpreta(risposta, contesto);
        Assert.Equal(3, p.Assegnazioni.Count);
        Assert.Equal(2, (await wbs.ElencoAsync(id)).Count);      // la proposta non salva nulla

        Assert.Equal(3, await wbs.ApplicaPropostaAsync(id, p.NuoviElementi, p.Assegnazioni, sostituisci: true));
        var elenco = await wbs.ElencoAsync(id);
        Assert.Equal(["1", "2", "2.1"], elenco.Select(e => e.Codice));
        Assert.Equal("Sicurezza e cantiere", elenco[0].Descrizione);
        Assert.Equal(3, elenco.Sum(e => e.VociDirette));
        var voci = await amb.Get<PreventivoService>().VociAsync(id);
        Assert.Equal("2", voci.Single(v => v.Id == contesto.Voci[0].Id).CodiceWbs);   // la vecchia 1.1 è sostituita

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => wbs.ApplicaPropostaAsync(id, [], [], sostituisci: true));
    }
}
