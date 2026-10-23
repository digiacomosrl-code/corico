using System.Diagnostics;
using System.Text.Json;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Corico.Core.Ai;

/// <summary>
/// Unico punto di accesso al servizio AI esterno (Modulo 3, §3 e §9). I moduli preparano una richiesta,
/// mostrano all'utente l'anteprima (testo inviato, modello, costo stimato) e solo dopo la conferma la inviano.
/// Con l'AI spenta non parte nessuna chiamata esterna.
/// </summary>
public interface IServizioAi
{
    Task<StatoAi> StatoAsync(CancellationToken ct = default);

    /// <summary>Prepara l'anteprima dell'invio: nessuna chiamata esterna.</summary>
    Task<AnteprimaAi> PreparaAsync(RichiestaAi richiesta, CancellationToken ct = default);

    /// <summary>Invia la richiesta mostrata nell'anteprima e la registra in CORE_RichiesteAi.</summary>
    Task<RispostaAi> InviaAsync(AnteprimaAi anteprima, CancellationToken ct = default);
}

/// <param name="Funzione">Funzione che chiede la lettura, es. «WBS», «Offerta»; compare nel registro.</param>
/// <param name="Istruzioni">Istruzioni per l'AI (prompt di sistema).</param>
/// <param name="Contenuto">Dati della singola richiesta: è il testo mostrato nell'anteprima e inviato.</param>
/// <param name="SchemaJson">JSON Schema della risposta: se presente la risposta è un JSON valido secondo lo schema.</param>
public sealed record RichiestaAi(string Funzione, Guid? CommessaId, string Istruzioni, string Contenuto, string? SchemaJson = null,
    int MaxTokenRisposta = 16000);

/// <param name="CostoMassimoStimato">Costo in euro con la risposta più lunga ammessa: il costo effettivo è di norma inferiore.</param>
/// <param name="Consentita">Falso se l'AI è spenta, manca la chiave, l'utente non è abilitato o si supererebbe il tetto mensile.</param>
public sealed record AnteprimaAi(Guid Id, RichiestaAi Richiesta, string Modello, int TokenIngressoStimati, decimal CostoMassimoStimato,
    decimal SpesaMese, decimal TettoMese, bool Consentita, string? Motivo);

/// <param name="Testo">Risposta dell'AI: JSON se la richiesta aveva uno schema.</param>
/// <param name="Costo">Costo in euro calcolato dai token effettivi.</param>
public sealed record RispostaAi(Guid RichiestaId, bool Riuscita, string? Testo, string? Errore, int TokenIngresso, int TokenUscita, decimal Costo);

public sealed record StatoAi(bool Attiva, bool ChiavePresente, string Modello, decimal SpesaMese, decimal TettoMese)
{
    public bool Utilizzabile => Attiva && ChiavePresente;
}

/// <summary>Configurazione dell'AI, modificabile solo dall'amministratore (pagina «Intelligenza artificiale»).</summary>
public sealed class ConfigurazioneAi
{
    public bool Attiva { get; set; }
    public string Modello { get; set; } = ModelloAi.Predefinito;

    /// <summary>Tetto di spesa mensile in euro: raggiunto il tetto le richieste si fermano fino al mese successivo.</summary>
    public decimal TettoMensile { get; set; } = 20m;

    /// <summary>Cambio dollaro → euro per convertire i prezzi del fornitore (espressi in dollari).</summary>
    public decimal CambioUsdEur { get; set; } = 0.92m;
}

/// <summary>Modelli ammessi con il prezzo del fornitore in dollari per milione di token (ingresso, uscita).</summary>
public sealed record ModelloAi(string Codice, string Nome, decimal PrezzoIngresso, decimal PrezzoUscita)
{
    public const string Predefinito = "claude-opus-5-5";

    public static readonly IReadOnlyList<ModelloAi> Elenco =
    [
        new(Predefinito, "Claude Opus 5.5 (consigliato)", 4m, 20m),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5", 2m, 10m),
        new("claude-haiku-5-5", "Claude Haiku 5.5 (economico)", 0.10m, 0.50m),
    ];

    public static ModelloAi Trova(string? codice) => Elenco.FirstOrDefault(m => m.Codice == codice) ?? Elenco[0];
}

public sealed class ServizioAi(
    IDbContextFactory<CoreDbContext> fabbrica,
    ImpostazioniService impostazioni,
    IArchivioChiaveAi chiavi,
    IClienteAi cliente,
    IUtenteCorrente utente,
    TimeProvider orologio,
    ILogger<ServizioAi> log) : IServizioAi
{
    public const string ChiaveConfigurazione = "Ai.Configurazione";

    private readonly Lock _blocco = new();

    /// <summary>Costo massimo delle richieste in corso, già impegnato sul tetto mensile.</summary>
    private decimal _impegnato;

    public async Task<ConfigurazioneAi> LeggiConfigurazioneAsync(CancellationToken ct = default)
    {
        var json = await impostazioni.LeggiAsync(ChiaveConfigurazione, ct);
        return string.IsNullOrEmpty(json) ? new ConfigurazioneAi() : JsonSerializer.Deserialize<ConfigurazioneAi>(json) ?? new ConfigurazioneAi();
    }

    public async Task SalvaConfigurazioneAsync(ConfigurazioneAi configurazione, CancellationToken ct = default)
    {
        SoloAmministratore();
        if (configurazione.TettoMensile < 0) throw new OperazioneNonValidaException("Il tetto mensile non può essere negativo.");
        if (configurazione.CambioUsdEur <= 0) throw new OperazioneNonValidaException("Il cambio dollaro/euro deve essere maggiore di zero.");
        configurazione.Modello = ModelloAi.Trova(configurazione.Modello).Codice;
        await impostazioni.ScriviAsync(ChiaveConfigurazione, JsonSerializer.Serialize(configurazione), ct);
    }

    public bool ChiavePresente => chiavi.Presente;

    public void SalvaChiave(string chiave)
    {
        SoloAmministratore();
        if (string.IsNullOrWhiteSpace(chiave)) throw new OperazioneNonValidaException("Inserisci la chiave d'accesso.");
        chiavi.Salva(chiave.Trim());
    }

    public void RimuoviChiave()
    {
        SoloAmministratore();
        chiavi.Rimuovi();
    }

    public async Task<StatoAi> StatoAsync(CancellationToken ct = default)
    {
        var conf = await LeggiConfigurazioneAsync(ct);
        return new StatoAi(conf.Attiva, chiavi.Presente, ModelloAi.Trova(conf.Modello).Codice, await SpesaMeseAsync(ct), conf.TettoMensile);
    }

    public async Task<AnteprimaAi> PreparaAsync(RichiestaAi richiesta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(richiesta);
        var conf = await LeggiConfigurazioneAsync(ct);
        var modello = ModelloAi.Trova(conf.Modello);
        var token = StimaToken(richiesta);
        var costo = Costo(modello, conf, token, richiesta.MaxTokenRisposta);
        var spesa = await SpesaMeseAsync(ct);
        decimal impegnato;
        lock (_blocco) impegnato = _impegnato;
        var motivo = Impedimento(conf, spesa + impegnato, costo);
        return new AnteprimaAi(Guid.NewGuid(), richiesta, modello.Codice, token, costo, spesa, conf.TettoMensile, motivo is null, motivo);
    }

    public async Task<RispostaAi> InviaAsync(AnteprimaAi anteprima, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(anteprima);
        var conf = await LeggiConfigurazioneAsync(ct);
        var modello = ModelloAi.Trova(anteprima.Modello);
        var richiesta = anteprima.Richiesta;
        var massimo = Costo(modello, conf, StimaToken(richiesta), richiesta.MaxTokenRisposta);
        var spesa = await SpesaMeseAsync(ct);

        // Verifica e impegno del tetto in un solo passo, così due invii contemporanei non lo superano.
        string? motivo;
        lock (_blocco)
        {
            motivo = Impedimento(conf, spesa + _impegnato, massimo);
            if (motivo is null) _impegnato += massimo;
        }
        if (motivo is not null) throw new OperazioneNonValidaException(motivo);

        var cronometro = Stopwatch.StartNew();
        RispostaAi risposta;
        try
        {
            var esito = await cliente.InviaAsync(new ChiamataAi(chiavi.Leggi()!, modello.Codice, richiesta.Istruzioni, richiesta.Contenuto,
                richiesta.SchemaJson, richiesta.MaxTokenRisposta), ct);
            var costo = Costo(modello, conf, esito.TokenIngresso, esito.TokenUscita);
            risposta = esito.Errore is null
                ? new RispostaAi(anteprima.Id, true, esito.Testo, null, esito.TokenIngresso, esito.TokenUscita, costo)
                : new RispostaAi(anteprima.Id, false, null, esito.Errore, esito.TokenIngresso, esito.TokenUscita, costo);
            await RegistraAsync(anteprima, esito.Rifiutata ? EsitoRichiestaAi.Rifiutata : esito.Errore is null ? EsitoRichiestaAi.Riuscita : EsitoRichiestaAi.Errore,
                risposta, (int)cronometro.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Richiesta AI {Funzione} non riuscita", richiesta.Funzione);
            risposta = new RispostaAi(anteprima.Id, false, null, $"Il servizio AI non ha risposto: {ex.Message}", 0, 0, 0m);
            await RegistraAsync(anteprima, EsitoRichiestaAi.Errore, risposta, (int)cronometro.Elapsed.TotalSeconds);
        }
        finally
        {
            lock (_blocco) _impegnato -= massimo;
        }
        return risposta;
    }

    /// <summary>Richieste del registro, dalla più recente (solo amministratore).</summary>
    public async Task<IReadOnlyList<RichiestaAiRegistrata>> RegistroAsync(int massimo = 200, string? utenteFiltro = null, Guid? commessaId = null,
        CancellationToken ct = default)
    {
        SoloAmministratore();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = db.RichiesteAi.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(utenteFiltro)) q = q.Where(r => r.Utente == utenteFiltro);
        if (commessaId is not null) q = q.Where(r => r.CommessaId == commessaId);
        return await q.OrderByDescending(r => r.DataOra).Take(massimo).ToListAsync(ct);
    }

    /// <summary>Spesa in euro delle richieste del mese corrente (ora locale del PC sede).</summary>
    public async Task<decimal> SpesaMeseAsync(CancellationToken ct = default)
    {
        var adesso = orologio.GetLocalNow();
        var inizio = new DateTimeOffset(adesso.Year, adesso.Month, 1, 0, 0, 0, adesso.Offset).UtcDateTime;
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        // SQLite non somma i decimali: la somma si fa in memoria (poche righe al mese).
        var costi = await db.RichiesteAi.AsNoTracking().Where(r => r.DataOra >= inizio).Select(r => r.Costo).ToListAsync(ct);
        return costi.Sum();
    }

    private string? Impedimento(ConfigurazioneAi conf, decimal spesaGiaImpegnata, decimal costo)
    {
        if (!conf.Attiva) return "L'intelligenza artificiale è spenta: l'amministratore può attivarla nella pagina «Intelligenza artificiale».";
        if (!chiavi.Presente) return "Manca la chiave d'accesso al servizio AI: l'amministratore la inserisce nella pagina «Intelligenza artificiale».";
        if (utente.Identita is { } chi)
        {
            if (!chi.Attivo) return "Il tuo utente è stato disattivato.";
            if (chi.Ruolo == RuoloUtente.Consultazione) return "Il tuo profilo è di sola consultazione: non puoi avviare letture con l'AI.";
        }
        if (spesaGiaImpegnata + costo > conf.TettoMensile)
            return $"Questa richiesta (fino a {costo:N2} €) supererebbe il tetto di spesa mensile di {conf.TettoMensile:N2} € " +
                   $"(già spesi {spesaGiaImpegnata:N2} €). L'amministratore può alzare il tetto.";
        return null;
    }

    private async Task RegistraAsync(AnteprimaAi anteprima, EsitoRichiestaAi esito, RispostaAi risposta, int secondi)
    {
        try
        {
            await using var db = await fabbrica.CreateDbContextAsync();
            // Il registro è già la traccia completa: niente voce di storico che duplichi il testo inviato.
            db.InserimentiSoloInRiepilogo = true;
            db.RichiesteAi.Add(new RichiestaAiRegistrata
            {
                Id = anteprima.Id,
                DataOra = orologio.GetUtcNow().UtcDateTime,
                Utente = utente.Nome,
                Funzione = anteprima.Richiesta.Funzione,
                CommessaId = anteprima.Richiesta.CommessaId,
                Modello = anteprima.Modello,
                Contenuto = anteprima.Richiesta.Contenuto,
                CaratteriInviati = anteprima.Richiesta.Istruzioni.Length + anteprima.Richiesta.Contenuto.Length,
                TokenIngresso = risposta.TokenIngresso,
                TokenUscita = risposta.TokenUscita,
                Costo = risposta.Costo,
                Esito = esito,
                Messaggio = risposta.Errore,
                DurataSecondi = secondi,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Registrazione della richiesta AI {Id} non riuscita", anteprima.Id);
        }
    }

    /// <summary>Stima prudente: circa 3 caratteri per token per l'italiano, più le istruzioni e lo schema.</summary>
    internal static int StimaToken(RichiestaAi r) =>
        (int)Math.Ceiling((r.Istruzioni.Length + r.Contenuto.Length + (r.SchemaJson?.Length ?? 0)) / 3.0) + 50;

    internal static decimal Costo(ModelloAi modello, ConfigurazioneAi conf, int tokenIngresso, int tokenUscita) =>
        Math.Round((tokenIngresso * modello.PrezzoIngresso + tokenUscita * modello.PrezzoUscita) / 1_000_000m * conf.CambioUsdEur, 4);

    private void SoloAmministratore()
    {
        if (utente.Identita is { } chi && chi.Ruolo != RuoloUtente.Amministratore)
            throw new OperazioneNonValidaException("La configurazione dell'intelligenza artificiale è riservata all'amministratore.");
    }
}
