using Corico.Core.Ai;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>Proposta WBS dall'AI: contenuto inviato, lettura e controllo della risposta, applicazione in sostituzione.</summary>
public class WbsAiTests
{
    private static VocePerWbs Voce(int ordine, string tariffa, string descrizione, string? wbs = null) =>
        new(Guid.NewGuid(), ordine, tariffa, descrizione, "OPERE EDILI", "Finiture", wbs, 1234.56m * ordine, "m²");

    [Fact]
    public void Contenuto_numera_le_voci_senza_importi_e_accorcia_le_descrizioni()
    {
        var lunga = "Intonaco | civile " + new string('x', 500);
        var contesto = new ContestoPropostaWbs(Guid.NewGuid(), [], [Voce(2, "E.02", lunga), Voce(1, "E.01", "Scavo\na sezione")]);
        var righe = WbsAi.Contenuto(contesto).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("1 | E.01 | m² | OPERE EDILI > Finiture | Scavo a sezione", righe[2]);
        Assert.StartsWith("2 | E.02 | m² | OPERE EDILI > Finiture | Intonaco / civile", righe[3]);
        Assert.DoesNotContain("1234", WbsAi.Contenuto(contesto));      // mai prezzi né importi (M3 §7.1)
        var richiesta = WbsAi.Richiesta(contesto);
        Assert.Equal(("WBS", 16000), (richiesta.Funzione, richiesta.MaxTokenRisposta));
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

    /// <summary>Servizio AI simulato: registra cosa riceve e risponde con il testo dato.</summary>
    private sealed class AiFinta(string risposta, bool consentita = true) : IServizioAi
    {
        public List<RichiestaAi> Inviate { get; } = [];

        public Task<StatoAi> StatoAsync(CancellationToken ct = default) => Task.FromResult(new StatoAi(true, true, "finto", 0, 20));

        public Task<AnteprimaAi> PreparaAsync(RichiestaAi r, CancellationToken ct = default) =>
            Task.FromResult(new AnteprimaAi(Guid.NewGuid(), r, "finto", 1000, 0.40m, 1m, 20m, consentita, consentita ? null : "AI spenta"));

        public Task<RispostaAi> InviaAsync(AnteprimaAi a, CancellationToken ct = default)
        {
            if (!a.Consentita) throw new OperazioneNonValidaException(a.Motivo!);
            Inviate.Add(a.Richiesta);
            return Task.FromResult(new RispostaAi(a.Id, risposta != "", risposta, risposta == "" ? "servizio non raggiungibile" : null, 1000, 200, 0.10m));
        }
    }

    [Fact]
    public async Task Anteprima_senza_invio_poi_invio_confermato_e_proposta()
    {
        var finta = new AiFinta("""
            { "tipologia": "Manutenzione", "criteri": "Sicurezza prima.", "elementi": [
              { "codice": "1", "descrizione": "Sicurezza", "motivo": "Prima fase", "voci": [3] },
              { "codice": "2", "descrizione": "Opere edili", "motivo": "Poi", "voci": [1, 2] } ] }
            """);
        await using var amb = await AmbienteDiProva.CreaAsync(s => s.AddSingleton<IServizioAi>(finta), new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        await amb.Get<ImportazioneComputoService>().ConfermaAsync(id, XpweDiProva.Contenuto, "prova.xpwe", null);
        var wbs = amb.Get<WbsService>();

        var (anteprima, contesto) = await wbs.PreparaPropostaAiAsync(id);
        Assert.Empty(finta.Inviate);                                  // l'anteprima non invia nulla
        Assert.Equal(id, anteprima.Richiesta.CommessaId);
        Assert.Equal(3, contesto.Voci.Count);

        var (proposta, risposta) = await wbs.InviaPropostaAiAsync(anteprima, contesto);
        Assert.Single(finta.Inviate);
        Assert.Equal(0.10m, risposta.Costo);
        Assert.Equal(["1", "2"], proposta.NuoviElementi.Select(e => e.Codice));
        Assert.Equal(3, proposta.Assegnazioni.Count);
        Assert.Empty(await wbs.ElencoAsync(id));                      // nulla salvato prima della conferma

        await using var amb2 = await AmbienteDiProva.CreaAsync(s => s.AddSingleton<IServizioAi>(new AiFinta("")), new PreventivoModulo());
        var id2 = (await amb2.Get<CommesseService>().CreaAsync("Altra")).Id;
        await amb2.Get<ImportazioneComputoService>().ConfermaAsync(id2, XpweDiProva.Contenuto, "prova.xpwe", null);
        var (a2, c2) = await amb2.Get<WbsService>().PreparaPropostaAiAsync(id2);
        var errore = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => amb2.Get<WbsService>().InviaPropostaAiAsync(a2, c2));
        Assert.Contains("non raggiungibile", errore.Message);
    }
}
