using Corico.Modules.Preventivo.Dati;
using Microsoft.EntityFrameworkCore;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class PreventivoServiceTests
{
    private static async Task<(AmbienteDiProva Amb, Guid CommessaId)> PreparaAsync()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var commessa = await amb.Get<CommesseService>().CreaAsync("Commessa di prova");
        return (amb, commessa.Id);
    }

    [Fact]
    public async Task Importazione_crea_computo_attivo_documento_e_backup()
    {
        var (amb, id) = await PreparaAsync();
        await using var _ = amb;

        var esito = await amb.Get<ImportazioneComputoService>().ConfermaXpweAsync(id, XpweDiProva.Contenuto, "prova.xpwe", 5000m);

        Assert.Equal(3, esito.Voci);
        Assert.NotNull(esito.Backup);
        Assert.True(File.Exists(esito.Backup));
        var documenti = await amb.Get<DocumentiService>().ElencoAsync(id);
        Assert.Contains(documenti, d => d.Id == esito.DocumentoId && d.Tipo == "Computo metrico");

        var stato = await amb.Get<PreventivoService>().StatoAsync(id);
        Assert.Equal("prova.xpwe", stato.Computo!.NomeFile);
        Assert.Equal(5000m, stato.Computo.TotaleDichiarato);
        Assert.Equal(XpweDiProva.TotaleAtteso, stato.Quadro.ImportoLavoriLordo);
        Assert.Equal(2000m, stato.Quadro.ImportoNonSoggetto);
        Assert.Equal(1, stato.Quadro.VociSenzaPrezzo);

        var voci = await amb.Get<PreventivoService>().VociAsync(id);
        Assert.Equal(["Intonaco civile", "Ponteggio metallico", "Opere extra da definire"], voci.Select(v => v.Descrizione));
        Assert.Equal("mq", voci[0].Unita);
        Assert.Equal(2, voci[0].RigheMisura);
        Assert.Equal(100m, voci[1].PercentualeNonSoggetta);
        Assert.True(voci[2].SenzaElencoPrezzi);
        Assert.Equal("SICUREZZA", voci[1].SuperCategoria);

        // Lo storico riporta un'unica operazione riepilogativa, non una riga per ogni voce importata.
        await using var db = await amb.Get<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync();
        var storico = await db.Storico.Where(s => s.CommessaId == id && s.Entita != "Commessa" && s.Entita != "Documento").ToListAsync();
        Assert.Single(storico, s => s.Operazione == "Importazione");
        Assert.DoesNotContain(storico, s => s.Entita == "VoceComputo");
    }

    [Fact]
    public async Task Il_ribasso_unico_si_applica_solo_alla_parte_soggetta()
    {
        var (amb, id) = await PreparaAsync();
        await using var _ = amb;
        await amb.Get<ImportazioneComputoService>().ConfermaXpweAsync(id, XpweDiProva.Contenuto, "prova.xpwe", null);
        var servizio = amb.Get<PreventivoService>();

        var stato = await servizio.StatoAsync(id);
        await servizio.ImpostaRibassoAsync(id, stato.Versione, 21.5m);
        stato = await servizio.StatoAsync(id);

        Assert.Equal(21.5m, stato.RibassoPercentuale);
        Assert.Equal(120.5m * 25.40m * 0.785m + 2000m, stato.Quadro.ImportoNetto);
        Assert.Equal(120.5m * 25.40m * 0.215m, stato.Quadro.ImportoRibasso);
    }

    [Fact]
    public async Task Modifica_concorrente_del_ribasso_viene_segnalata()
    {
        var (amb, id) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<PreventivoService>();
        var letto = await servizio.StatoAsync(id);

        await servizio.ImpostaRibassoAsync(id, letto.Versione, 10m);
        await Assert.ThrowsAsync<ConflittoModificaException>(() => servizio.ImpostaRibassoAsync(id, letto.Versione, 20m));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => servizio.ImpostaRibassoAsync(id, letto.Versione + 1, 100m));
    }

    [Fact]
    public async Task Una_nuova_importazione_sostituisce_il_computo_attivo_e_conserva_il_precedente()
    {
        var (amb, id) = await PreparaAsync();
        await using var _ = amb;
        var import = amb.Get<ImportazioneComputoService>();

        await import.ConfermaXpweAsync(id, XpweDiProva.Contenuto, "prima.xpwe", null);
        await import.ConfermaXpweAsync(id, XpweDiProva.Contenuto, "seconda.xpwe", null);

        var computi = await amb.Get<PreventivoService>().ComputiAsync(id);
        Assert.Equal(2, computi.Count);
        Assert.Equal("seconda.xpwe", (await amb.Get<PreventivoService>().StatoAsync(id)).Computo!.NomeFile);
        Assert.Equal(3, (await amb.Get<PreventivoService>().VociAsync(id)).Count);
    }

    [Fact]
    public async Task Voci_manuali_modifica_quota_non_soggetta_e_archiviazione()
    {
        var (amb, id) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<PreventivoService>();

        await servizio.SalvaVoceAsync(id, new DatiVoce { Descrizione = "Scavo", Unita = "m3", Quantita = 10, Prezzo = 15 });
        await servizio.SalvaVoceAsync(id, new DatiVoce { Descrizione = "Oneri sicurezza", Unita = "a.c.", Quantita = 1, Prezzo = 800 });
        var voci = await servizio.VociAsync(id);
        Assert.Equal("mc", voci[0].Unita);
        Assert.False(voci[0].SenzaElencoPrezzi);

        Assert.Equal(1, await servizio.ImpostaNonSoggettaAsync(id, [voci[1].Id], 100m));
        await servizio.ImpostaRibassoAsync(id, (await servizio.StatoAsync(id)).Versione, 10m);
        Assert.Equal(150m * 0.9m + 800m, (await servizio.StatoAsync(id)).Quadro.ImportoNetto);

        await servizio.ArchiviaVoceAsync(id, voci[0].Id, voci[0].Versione);
        Assert.Single(await servizio.VociAsync(id));
        Assert.Single(await servizio.VociAsync(id, "sicur"));

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
            servizio.SalvaVoceAsync(id, new DatiVoce { Descrizione = " ", Quantita = 1, Prezzo = 1 }));
    }
}
