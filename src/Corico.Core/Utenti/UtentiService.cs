using System.Collections.Concurrent;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Utenti;

public sealed record EsitoAccesso(IdentitaUtente? Identita, string? Errore)
{
    public bool Riuscito => Identita is not null;
}

public sealed record RigaUtente(Guid Id, int Versione, string NomeUtente, string NomeCompleto, string? Email, RuoloUtente Ruolo,
    bool Attivo, bool CambioPasswordObbligatorio, DateTime? UltimoAccesso);

/// <summary>
/// Utenti, accesso e password (sez. 4). Blocco temporaneo dopo <see cref="TentativiMassimi"/> tentativi errati;
/// l'ultimo amministratore attivo non si può disattivare né declassare.
/// </summary>
public sealed class UtentiService(IDbContextFactory<CoreDbContext> fabbrica, TimeProvider orologio)
{
    public const int TentativiMassimi = 5;
    public static readonly TimeSpan DurataBlocco = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (int Errori, DateTimeOffset? BloccatoFino)> _tentativi = new();
    private readonly ConcurrentDictionary<Guid, IdentitaUtente?> _cache = new();

    public async Task<bool> EsistonoUtentiAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Utenti.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<RigaUtente>> ElencoAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return (await db.Utenti.AsNoTracking().ToListAsync(ct))
            .OrderBy(u => !u.Attivo).ThenBy(u => u.NomeCompleto, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new RigaUtente(u.Id, u.Versione, u.NomeUtente, u.NomeCompleto, u.Email, u.Ruolo, u.Attivo,
                u.CambioPasswordObbligatorio, u.UltimoAccesso))
            .ToList();
    }

    /// <summary>Identità aggiornata dell'utente (ruolo, stato), con cache invalidata a ogni modifica degli utenti.</summary>
    public async Task<IdentitaUtente?> IdentitaAsync(Guid id, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(id, out var nota)) return nota;
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = await db.Utenti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var identita = u?.Identita();
        _cache[id] = identita;
        return identita;
    }

    /// <summary>Primo avvio: crea l'amministratore. Possibile solo finché non esiste alcun utente.</summary>
    public async Task<IdentitaUtente> CreaPrimoAmministratoreAsync(string nomeUtente, string nomeCompleto, string password, CancellationToken ct = default)
    {
        Password.ControllaRequisiti(password);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        if (await db.Utenti.AnyAsync(ct))
            throw new OperazioneNonValidaException("L'amministratore esiste già: accedi con il tuo utente.");
        var u = Nuovo(nomeUtente, nomeCompleto, null, RuoloUtente.Amministratore, password, cambioObbligatorio: false);
        db.Utenti.Add(u);
        using (UtenteCorrente.Imposta(u.Identita()))
        {
            db.RegistraOperazione("Creazione amministratore", nameof(Utente), u.Id, null, $"Primo amministratore: {u.NomeUtente}");
            await db.SaveChangesAsync(ct);
        }
        _cache.Clear();
        return u.Identita();
    }

    public async Task<EsitoAccesso> AccediAsync(string nomeUtente, string password, CancellationToken ct = default)
    {
        var chiave = Normalizza(nomeUtente);
        var adesso = orologio.GetUtcNow();
        if (_tentativi.TryGetValue(chiave, out var t) && t.BloccatoFino > adesso)
        {
            var minuti = (int)Math.Ceiling((t.BloccatoFino.Value - adesso).TotalMinutes);
            return new(null, $"Troppi tentativi errati: accesso bloccato per {minuti} minut{(minuti == 1 ? "o" : "i")}.");
        }

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = await db.Utenti.FirstOrDefaultAsync(x => x.NomeUtente == chiave, ct);
        if (u is null || !u.Attivo || !Password.Verifica(password, u.HashPassword))
        {
            var errori = (_tentativi.TryGetValue(chiave, out var p) ? p.Errori : 0) + 1;
            var blocco = errori >= TentativiMassimi ? adesso + DurataBlocco : (DateTimeOffset?)null;
            _tentativi[chiave] = (blocco is null ? errori : 0, blocco);
            db.RegistraOperazione("Accesso non riuscito", nameof(Utente), u?.Id, null,
                $"Utente «{chiave}»{(u is { Attivo: false } ? " disattivato" : "")}{(blocco is null ? "" : " – accesso bloccato per 5 minuti")}");
            await db.SaveChangesAsync(ct);
            return new(null, blocco is null ? "Utente o password non corretti." : "Troppi tentativi errati: accesso bloccato per 5 minuti.");
        }

        _tentativi.TryRemove(chiave, out _);
        u.UltimoAccesso = adesso.UtcDateTime;
        using (UtenteCorrente.Imposta(u.Identita()))
        {
            db.RegistraOperazione("Accesso", nameof(Utente), u.Id, null, $"Accesso di {u.NomeCompleto} ({u.NomeUtente})");
            await db.SaveChangesAsync(ct);
        }
        _cache.TryRemove(u.Id, out _);
        return new(u.Identita(), null);
    }

    public async Task RegistraUscitaAsync(Guid id, string motivo, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = await db.Utenti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return;
        using (UtenteCorrente.Imposta(u.Identita()))
        {
            db.RegistraOperazione("Uscita", nameof(Utente), u.Id, null, $"{u.NomeCompleto}: {motivo}");
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Nuovo utente con password provvisoria, da cambiare al primo accesso. Restituisce la password provvisoria.</summary>
    public async Task<string> CreaAsync(string nomeUtente, string nomeCompleto, string? email, RuoloUtente ruolo, CancellationToken ct = default)
    {
        var provvisoria = Password.Provvisoria();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = Nuovo(nomeUtente, nomeCompleto, email, ruolo, provvisoria, cambioObbligatorio: true);
        if (await db.Utenti.AnyAsync(x => x.NomeUtente == u.NomeUtente, ct))
            throw new OperazioneNonValidaException($"Esiste già un utente «{u.NomeUtente}».");
        db.Utenti.Add(u);
        await db.SaveChangesAsync(ct);
        _cache.Clear();
        return provvisoria;
    }

    public async Task ModificaAsync(Guid id, int versione, string nomeCompleto, string? email, RuoloUtente ruolo, bool attivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nomeCompleto)) throw new OperazioneNonValidaException("Il nome e cognome è obbligatorio.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = await db.Utenti.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new OperazioneNonValidaException("L'utente non esiste.");
        db.Entry(u).Property(x => x.Versione).OriginalValue = versione;
        var restaAmministratore = attivo && ruolo == RuoloUtente.Amministratore;
        if (u is { Ruolo: RuoloUtente.Amministratore, Attivo: true } && !restaAmministratore
            && !await db.Utenti.AnyAsync(x => x.Id != id && x.Attivo && x.Ruolo == RuoloUtente.Amministratore, ct))
            throw new OperazioneNonValidaException("Deve restare almeno un amministratore attivo.");
        u.NomeCompleto = nomeCompleto.Trim();
        u.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        u.Ruolo = ruolo;
        u.Attivo = attivo;
        await db.SaveChangesAsync(ct);
        _cache.Clear();
    }

    /// <summary>Reimpostazione da parte dell'amministratore: nuova password provvisoria, da cambiare al primo accesso.</summary>
    public async Task<string> ReimpostaPasswordAsync(Guid id, CancellationToken ct = default)
    {
        var provvisoria = Password.Provvisoria();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = await db.Utenti.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new OperazioneNonValidaException("L'utente non esiste.");
        u.HashPassword = Password.Impronta(provvisoria);
        u.CambioPasswordObbligatorio = true;
        db.RegistraOperazione("Reimpostazione password", nameof(Utente), u.Id, null, $"Password provvisoria per {u.NomeCompleto}");
        await db.SaveChangesAsync(ct);
        _tentativi.TryRemove(u.NomeUtente, out _);
        _cache.Clear();
        return provvisoria;
    }

    /// <summary>Reimpostazione di emergenza dal PC sede (riga di comando), anche senza amministratore collegato.</summary>
    public async Task<string> ReimpostaPasswordAsync(string nomeUtente, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var chiave = Normalizza(nomeUtente);
        var id = await db.Utenti.Where(x => x.NomeUtente == chiave).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
            ?? throw new OperazioneNonValidaException($"L'utente «{chiave}» non esiste.");
        var u = await db.Utenti.FirstAsync(x => x.Id == id, ct);
        if (!u.Attivo) { u.Attivo = true; await db.SaveChangesAsync(ct); }
        return await ReimpostaPasswordAsync(id, ct);
    }

    public async Task CambiaPasswordAsync(Guid id, string attuale, string nuova, CancellationToken ct = default)
    {
        Password.ControllaRequisiti(nuova);
        if (attuale == nuova) throw new OperazioneNonValidaException("La nuova password deve essere diversa da quella attuale.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var u = await db.Utenti.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new OperazioneNonValidaException("L'utente non esiste.");
        if (!Password.Verifica(attuale, u.HashPassword)) throw new OperazioneNonValidaException("La password attuale non è corretta.");
        u.HashPassword = Password.Impronta(nuova);
        u.CambioPasswordObbligatorio = false;
        db.RegistraOperazione("Cambio password", nameof(Utente), u.Id, null, $"{u.NomeCompleto} ha cambiato la propria password");
        await db.SaveChangesAsync(ct);
        _cache.TryRemove(id, out _);
    }

    private Utente Nuovo(string nomeUtente, string nomeCompleto, string? email, RuoloUtente ruolo, string password, bool cambioObbligatorio)
    {
        var chiave = Normalizza(nomeUtente);
        if (chiave.Length < 2 || chiave.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new OperazioneNonValidaException("Il nome utente deve avere almeno 2 caratteri tra lettere, numeri, punto, trattino e trattino basso (es. mario.rossi).");
        if (string.IsNullOrWhiteSpace(nomeCompleto)) throw new OperazioneNonValidaException("Il nome e cognome è obbligatorio.");
        return new Utente
        {
            NomeUtente = chiave, NomeCompleto = nomeCompleto.Trim(), Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Ruolo = ruolo, HashPassword = Password.Impronta(password), CambioPasswordObbligatorio = cambioObbligatorio,
        };
    }

    /// <summary>Accessi, uscite e modifiche degli utenti, dal più recente.</summary>
    public async Task<IReadOnlyList<VoceStorico>> RegistroAccessiAsync(int quante = 200, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Storico.AsNoTracking().Where(v => v.Entita == nameof(Utente))
            .OrderByDescending(v => v.Id).Take(quante).ToListAsync(ct);
    }

    public static string Normalizza(string nomeUtente) => (nomeUtente ?? "").Trim().ToLowerInvariant();
}
