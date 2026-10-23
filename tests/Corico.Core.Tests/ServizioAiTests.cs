using System.Net;
using System.Text;
using Corico.Core.Ai;
using Corico.Core.Dominio;
using Corico.Core.Utenti;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Core.Tests;

public class ServizioAiTests
{
    private static readonly IdentitaUtente Admin = new(Guid.NewGuid(), "admin", "Amministratore", RuoloUtente.Amministratore);
    private static readonly IdentitaUtente Operatore = new(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);
    private static readonly IdentitaUtente Lettore = new(Guid.NewGuid(), "anna", "Anna Bianchi", RuoloUtente.Consultazione);

    private static readonly RichiestaAi Richiesta = new("WBS", Guid.NewGuid(), "Proponi la WBS.", "01.A01 Scavo di sbancamento m³", "{\"type\":\"object\"}");

    private sealed class ChiaveFinta : IArchivioChiaveAi
    {
        public string? Valore { get; set; }
        public bool Presente => Valore is not null;
        public string? Leggi() => Valore;
        public void Salva(string chiave) => Valore = chiave;
        public void Rimuovi() => Valore = null;
    }

    private sealed class ClienteFinto : IClienteAi
    {
        public List<ChiamataAi> Chiamate { get; } = [];
        public Func<ChiamataAi, EsitoChiamataAi> Risposta { get; set; } = _ => new("{\"ok\":true}", null, 1000, 500);

        public Task<EsitoChiamataAi> InviaAsync(ChiamataAi chiamata, CancellationToken ct)
        {
            Chiamate.Add(chiamata);
            return Task.FromResult(Risposta(chiamata));
        }
    }

    private static async Task<(AmbienteDiProva Ambiente, ServizioAi Ai, ChiaveFinta Chiave, ClienteFinto Cliente)> CreaAsync()
    {
        var chiave = new ChiaveFinta();
        var cliente = new ClienteFinto();
        var ambiente = await AmbienteDiProva.CreaAsync(s =>
        {
            s.AddSingleton<IArchivioChiaveAi>(chiave);
            s.AddSingleton<IClienteAi>(cliente);
        });
        return (ambiente, ambiente.Get<ServizioAi>(), chiave, cliente);
    }

    private static async Task AttivaAsync(ServizioAi ai, ChiaveFinta chiave, decimal tetto = 20m)
    {
        using (UtenteCorrente.Imposta(Admin))
            await ai.SalvaConfigurazioneAsync(new ConfigurazioneAi { Attiva = true, TettoMensile = tetto });
        chiave.Valore = "sk-prova";
    }

    [Fact]
    public async Task Con_AI_spenta_nessuna_chiamata_esterna()
    {
        var (ambiente, ai, chiave, cliente) = await CreaAsync();
        await using var _ = ambiente;
        chiave.Valore = "sk-prova";

        var anteprima = await ai.PreparaAsync(Richiesta);

        Assert.False(anteprima.Consentita);
        Assert.Contains("spenta", anteprima.Motivo);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ai.InviaAsync(anteprima));
        Assert.Empty(cliente.Chiamate);
        Assert.Empty(await ai.RegistroAsync());
    }

    [Fact]
    public async Task Senza_chiave_la_richiesta_non_parte()
    {
        var (ambiente, ai, chiave, cliente) = await CreaAsync();
        await using var _ = ambiente;
        await AttivaAsync(ai, chiave);
        chiave.Valore = null;

        var anteprima = await ai.PreparaAsync(Richiesta);

        Assert.False(anteprima.Consentita);
        Assert.Contains("chiave", anteprima.Motivo);
        Assert.Empty(cliente.Chiamate);
    }

    [Fact]
    public async Task Invio_manda_solo_il_contenuto_dell_anteprima_e_lo_registra()
    {
        var (ambiente, ai, chiave, cliente) = await CreaAsync();
        await using var _ = ambiente;
        await AttivaAsync(ai, chiave);

        RispostaAi risposta;
        using (UtenteCorrente.Imposta(Operatore))
        {
            var anteprima = await ai.PreparaAsync(Richiesta);
            Assert.True(anteprima.Consentita);
            Assert.Equal(ModelloAi.Predefinito, anteprima.Modello);
            Assert.True(anteprima.CostoMassimoStimato > 0);
            risposta = await ai.InviaAsync(anteprima);
        }

        Assert.True(risposta.Riuscita);
        Assert.Equal("{\"ok\":true}", risposta.Testo);
        var chiamata = Assert.Single(cliente.Chiamate);
        Assert.Equal(Richiesta.Contenuto, chiamata.Contenuto);
        Assert.Equal(Richiesta.SchemaJson, chiamata.SchemaJson);
        Assert.Equal("sk-prova", chiamata.Chiave);

        // 1000 token in ingresso a 4 $ e 500 in uscita a 20 $ per milione, al cambio 0,92.
        Assert.Equal(0.0129m, risposta.Costo);
        IReadOnlyList<RichiestaAiRegistrata> registro;
        using (UtenteCorrente.Imposta(Admin)) registro = await ai.RegistroAsync();
        var voce = Assert.Single(registro);
        Assert.Equal("Mario Rossi", voce.Utente);
        Assert.Equal("WBS", voce.Funzione);
        Assert.Equal(Richiesta.CommessaId, voce.CommessaId);
        Assert.Equal(Richiesta.Contenuto, voce.Contenuto);
        Assert.Equal(EsitoRichiestaAi.Riuscita, voce.Esito);
        Assert.Equal(0.0129m, await ai.SpesaMeseAsync());
    }

    [Fact]
    public async Task Il_tetto_mensile_ferma_le_richieste()
    {
        var (ambiente, ai, chiave, cliente) = await CreaAsync();
        await using var _ = ambiente;
        await AttivaAsync(ai, chiave, tetto: 0.10m);

        // Con 16.000 token di risposta il costo massimo (circa 0,29 €) supera il tetto di 0,10 €.
        var anteprima = await ai.PreparaAsync(Richiesta);

        Assert.False(anteprima.Consentita);
        Assert.Contains("tetto", anteprima.Motivo);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ai.InviaAsync(anteprima));
        Assert.Empty(cliente.Chiamate);

        var piccola = await ai.PreparaAsync(Richiesta with { MaxTokenRisposta = 1000 });
        Assert.True(piccola.Consentita);
    }

    [Fact]
    public async Task La_consultazione_non_invia_e_non_configura()
    {
        var (ambiente, ai, chiave, cliente) = await CreaAsync();
        await using var _ = ambiente;
        await AttivaAsync(ai, chiave);

        using (UtenteCorrente.Imposta(Lettore))
        {
            var anteprima = await ai.PreparaAsync(Richiesta);
            Assert.False(anteprima.Consentita);
            Assert.Contains("consultazione", anteprima.Motivo);
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ai.InviaAsync(anteprima));
        }
        using (UtenteCorrente.Imposta(Operatore))
        {
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ai.SalvaConfigurazioneAsync(new ConfigurazioneAi { TettoMensile = 1000 }));
            Assert.Throws<OperazioneNonValidaException>(() => ai.SalvaChiave("sk-altra"));
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ai.RegistroAsync());
        }
        Assert.Empty(cliente.Chiamate);
        Assert.Equal("sk-prova", chiave.Valore);
    }

    [Fact]
    public async Task Un_errore_del_servizio_resta_nel_registro()
    {
        var (ambiente, ai, chiave, cliente) = await CreaAsync();
        await using var _ = ambiente;
        await AttivaAsync(ai, chiave);
        cliente.Risposta = _ => throw new HttpRequestException("rete assente");

        var risposta = await ai.InviaAsync(await ai.PreparaAsync(Richiesta));

        Assert.False(risposta.Riuscita);
        Assert.Contains("rete assente", risposta.Errore);
        var voce = Assert.Single(await ai.RegistroAsync());
        Assert.Equal(EsitoRichiestaAi.Errore, voce.Esito);
        Assert.Equal(0m, voce.Costo);
    }

    [Fact]
    public async Task Cliente_Anthropic_legge_la_risposta_in_streaming()
    {
        var gestore = new RispostaSimulata(Sse(
            ("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":1200,"output_tokens":1}}}"""),
            ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"elementi\":"}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"[]}"}}"""),
            ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
            ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":42}}"""),
            ("message_stop", """{"type":"message_stop"}""")));
        var cliente = new ClienteAnthropic(gestore);

        var esito = await cliente.InviaAsync(new ChiamataAi("sk-prova", ModelloAi.Predefinito, "Istruzioni", "Voci", "{\"type\":\"object\"}", 16000),
            CancellationToken.None);

        Assert.Null(esito.Errore);
        Assert.Equal("{\"elementi\":[]}", esito.Testo);
        Assert.Equal(1200, esito.TokenIngresso);
        Assert.Equal(42, esito.TokenUscita);
        Assert.Contains("\"fallbacks\":\"default\"", gestore.Corpo);
        Assert.Contains("\"json_schema\"", gestore.Corpo);
        Assert.Contains("\"content\":\"Voci\"", gestore.Corpo);
    }

    [Fact]
    public async Task Cliente_Anthropic_segnala_la_richiesta_declinata()
    {
        var gestore = new RispostaSimulata(Sse(
            ("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}"""),
            ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"refusal","stop_sequence":null},"usage":{"output_tokens":3}}"""),
            ("message_stop", """{"type":"message_stop"}""")));

        var esito = await new ClienteAnthropic(gestore).InviaAsync(new ChiamataAi("sk", ModelloAi.Predefinito, "i", "c", null, 100), CancellationToken.None);

        Assert.True(esito.Rifiutata);
        Assert.Null(esito.Testo);
        Assert.NotNull(esito.Errore);
    }

    private static string Sse(params (string Evento, string Dati)[] eventi) =>
        string.Concat(eventi.Select(e => $"event: {e.Evento}\ndata: {e.Dati}\n\n"));

    private sealed class RispostaSimulata(string corpoSse) : HttpMessageHandler
    {
        public string Corpo { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage richiesta, CancellationToken ct)
        {
            Corpo = await richiesta.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpoSse, Encoding.UTF8, "text/event-stream") };
        }
    }
}
