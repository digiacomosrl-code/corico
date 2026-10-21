using System.Text.Json;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Tests;

public class CommesseTests
{
    [Fact]
    public async Task Codici_progressivi_per_anno_e_mai_riutilizzati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();

        var a = await commesse.CreaAsync("Prima");
        var b = await commesse.CreaAsync("Seconda");
        await commesse.ImpostaArchiviazioneAsync(b.Id, b.Versione, true);
        var c = await commesse.CreaAsync("Terza");

        Assert.Equal("2026-001", a.Codice);
        Assert.Equal("2026-002", b.Codice);
        Assert.Equal("2026-003", c.Codice);

        amb.Orologio.SetUtcNow(new DateTimeOffset(2027, 1, 2, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal("2027-001", (await commesse.CreaAsync("Anno nuovo")).Codice);
    }

    [Fact]
    public async Task Descrizione_obbligatoria()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => amb.Get<CommesseService>().CreaAsync("  "));
    }

    [Fact]
    public async Task Le_modifiche_sono_registrate_nello_storico_con_utente_e_valori()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var c = await commesse.CreaAsync("Restauro");

        c.Responsabile = "DGS";
        c.ValoreContrattuale = 1000m;
        c = await commesse.AggiornaAsync(c);
        await commesse.CambiaStatoAsync(c.Id, c.Versione, StatoCommessa.Attiva);

        var storico = await amb.Get<StoricoService>().PerCommessaAsync(c.Id);
        Assert.Equal(3, storico.Count);
        Assert.All(storico, v => Assert.Equal(Environment.UserName, v.Utente));

        var modifica = storico.Single(v => v.Operazione == "Modifica" && v.ValoriSuccessivi!.Contains("Responsabile"));
        var dopo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modifica.ValoriSuccessivi!)!;
        Assert.Equal("DGS", dopo["Responsabile"].GetString());
        Assert.False(dopo.ContainsKey("Descrizione")); // solo i campi effettivamente cambiati

        var cambioStato = storico.First();
        Assert.Equal("Cambio di stato", cambioStato.Nota);
        Assert.Contains("\"Stato\":0", cambioStato.ValoriPrecedenti);
    }

    [Fact]
    public async Task Due_schede_aperte_la_seconda_riceve_un_avviso_di_conflitto()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var originale = await commesse.CreaAsync("Commessa");

        var scheda1 = originale.Clona();
        var scheda2 = originale.Clona();

        scheda1.Responsabile = "Primo";
        await commesse.AggiornaAsync(scheda1);

        scheda2.Responsabile = "Secondo";
        await Assert.ThrowsAsync<ConflittoModificaException>(() => commesse.AggiornaAsync(scheda2));

        Assert.Equal("Primo", (await commesse.OttieniAsync(originale.Id))!.Responsabile);
    }

    [Fact]
    public async Task Ricerca_ed_esclusione_delle_archiviate()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var c1 = await commesse.CreaAsync("CDD Caltagirone");
        await commesse.CreaAsync("Scuola Biancavilla");
        await commesse.ImpostaArchiviazioneAsync(c1.Id, c1.Versione, true);

        Assert.Empty(await commesse.ElencoAsync("caltagirone"));
        Assert.Single(await commesse.ElencoAsync("caltagirone", includiArchiviate: true));
        Assert.Single(await commesse.ElencoAsync("2026-002"));
    }

    [Fact]
    public async Task Date_incoerenti_rifiutate_e_dati_mancanti_segnalati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var c = await commesse.CreaAsync("Commessa");

        Assert.Contains("Cliente", CommesseService.DatiMancanti(c));

        c.DataInizioPrevista = new DateOnly(2026, 5, 1);
        c.DataFinePrevista = new DateOnly(2026, 4, 1);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => commesse.AggiornaAsync(c));
    }
}
