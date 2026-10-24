using System.Text.RegularExpressions;
using Corico.Core.Calcolo;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed class DatiAttore
{
    public Guid Id { get; set; }
    public int Versione { get; set; }
    public string Nominativo { get; set; } = "";
    public string? Titolo { get; set; }
    public string? Ente { get; set; }
    public string? Indirizzo { get; set; }
    public string? Cap { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? Email { get; set; }
    public string? Pec { get; set; }
    public string? Telefono { get; set; }
    public string? Note { get; set; }
    public bool Archiviato { get; set; }

    /// <summary>Solo lettura: chi ha modificato la scheda per ultimo e quando.</summary>
    public string UltimaModifica { get; set; } = "";

    /// <summary>«Ing. Mario Rossi».</summary>
    public string NomeCompleto => string.IsNullOrWhiteSpace(Titolo) ? Nominativo : $"{Titolo} {Nominativo}";

    public DatiAttore Clona() => (DatiAttore)MemberwiseClone();

    internal static DatiAttore Da(Attore a) => new()
    {
        Id = a.Id, Versione = a.Versione, Nominativo = a.Nominativo, Titolo = a.Titolo, Ente = a.Ente,
        Indirizzo = a.Indirizzo, Cap = a.Cap, Comune = a.Comune, Provincia = a.Provincia,
        Email = a.Email, Pec = a.Pec, Telefono = a.Telefono, Note = a.Note, Archiviato = a.Archiviato,
        UltimaModifica = Formato.UltimaModifica(a),
    };
}

/// <summary>Un incarico della commessa con i dati dell'attore, per la scheda «Dati generali».</summary>
public sealed record DatiIncarico(
    Guid Id, int Versione, Guid CommessaId, RuoloCommessa Ruolo, Guid AttoreId, string Nominativo, string? Titolo, string? Ente,
    string? Email, string? Pec, string? Telefono, DateOnly? Dal, DateOnly? Al, string? Note, bool InCarica, string UltimaModifica);

/// <summary>
/// Figura in carica in una commessa con tutti i recapiti: è il dato da usare per comunicazioni e lettere
/// (destinatario, indirizzo, email, PEC).
/// </summary>
public sealed record FiguraCommessa(
    RuoloCommessa Ruolo, Guid AttoreId, string Nominativo, string? Titolo, string? Ente,
    string? Indirizzo, string? Cap, string? Comune, string? Provincia,
    string? Email, string? Pec, string? Telefono, DateOnly? Dal, DateOnly? Al)
{
    public string NomeCompleto => string.IsNullOrWhiteSpace(Titolo) ? Nominativo : $"{Titolo} {Nominativo}";
}

/// <summary>Un incarico dell'attore in una commessa (per la scheda dell'attore).</summary>
public sealed record IncaricoDiAttore(Guid CommessaId, string CodiceCommessa, string DescrizioneCommessa, RuoloCommessa Ruolo, DateOnly? Dal, DateOnly? Al);

/// <summary>
/// Archivio degli attori (sezione «Attori») e loro incarichi nelle commesse: più soggetti per figura,
/// sostituzioni in corso d'opera con date di validità; ogni operazione resta nello storico.
/// </summary>
public sealed partial class AttoriService(IDbContextFactory<CoreDbContext> fabbrica, TimeProvider orologio)
{
    public async Task<IReadOnlyList<DatiAttore>> ElencoAsync(string? testo = null, bool includiArchiviati = false, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var query = db.Attori.AsNoTracking().AsQueryable();
        if (!includiArchiviati) query = query.Where(a => !a.Archiviato);
        var elenco = (await query.OrderBy(a => a.Nominativo).ToListAsync(ct)).Select(DatiAttore.Da);
        // Ricerca per parole come nei campi di scelta: «rossi comune» trova «Mario Rossi — Comune di Caltagirone».
        var parole = RicercaTesto.Parole(testo);
        return elenco.Where(a => RicercaTesto.Corrisponde($"{a.Titolo} {a.Nominativo} {a.Ente} {a.Email} {a.Pec} {a.Comune}", parole)).ToList();
    }

    public async Task<DatiAttore?> OttieniAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var a = await db.Attori.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : DatiAttore.Da(a);
    }

    /// <summary>Attori con la stessa email o PEC, o con nominativo simile. La segnalazione non blocca il salvataggio.</summary>
    public async Task<IReadOnlyList<PossibileDuplicato>> PossibiliDuplicatiAsync(DatiAttore dati, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var altri = await db.Attori.AsNoTracking().Where(a => a.Id != dati.Id).ToListAsync(ct);
        var nome = ControlloDuplicati.NormalizzaNome(dati.Nominativo);
        var recapiti = new[] { dati.Email, dati.Pec }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().ToLowerInvariant()).ToHashSet();
        var risultato = new List<PossibileDuplicato>();
        foreach (var a in altri)
        {
            if (new[] { a.Email, a.Pec }.Any(x => x is not null && recapiti.Contains(x.ToLowerInvariant())))
                risultato.Add(new(a.Id, a.Nominativo, "Stesso indirizzo email o PEC"));
            else if (nome.Length >= 3 && ControlloDuplicati.NomiSimili(nome, ControlloDuplicati.NormalizzaNome(a.Nominativo)))
                risultato.Add(new(a.Id, a.Nominativo, "Nominativo simile"));
        }
        return risultato;
    }

    public async Task<DatiAttore> SalvaAsync(DatiAttore dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Nominativo))
            throw new OperazioneNonValidaException("Il nominativo è obbligatorio.");
        ControllaIndirizzo(dati.Email, "L'indirizzo email");
        ControllaIndirizzo(dati.Pec, "L'indirizzo PEC");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        Attore a;
        if (dati.Id == Guid.Empty)
        {
            a = new Attore();
            db.Attori.Add(a);
        }
        else
        {
            a = await db.Attori.FirstOrDefaultAsync(x => x.Id == dati.Id, ct)
                ?? throw new OperazioneNonValidaException("L'attore non esiste.");
            db.Entry(a).Property(x => x.Versione).OriginalValue = dati.Versione;
        }

        a.Nominativo = dati.Nominativo.Trim();
        a.Titolo = Pulisci(dati.Titolo);
        a.Ente = Pulisci(dati.Ente);
        a.Indirizzo = Pulisci(dati.Indirizzo);
        a.Cap = Pulisci(dati.Cap);
        a.Comune = Pulisci(dati.Comune);
        a.Provincia = Pulisci(dati.Provincia)?.ToUpperInvariant();
        a.Email = Pulisci(dati.Email);
        a.Pec = Pulisci(dati.Pec);
        a.Telefono = Pulisci(dati.Telefono);
        a.Note = Pulisci(dati.Note);

        await db.SaveChangesAsync(ct);
        return DatiAttore.Da(a);
    }

    public async Task<DatiAttore> ImpostaArchiviazioneAsync(Guid id, int versione, bool archiviato, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var a = await db.Attori.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("L'attore non esiste.");
        db.Entry(a).Property(x => x.Versione).OriginalValue = versione;
        a.Archiviato = archiviato;
        await db.SaveChangesAsync(ct);
        return DatiAttore.Da(a);
    }

    /// <summary>Eliminazione definitiva (solo amministratore) di un attore mai usato in una commessa; altrimenti si archivia.</summary>
    public async Task EliminaAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var a = await db.Attori.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("L'attore non esiste.");
        if (await db.IncarichiCommessa.AnyAsync(i => i.AttoreId == id, ct))
            throw new OperazioneNonValidaException("L'attore compare nelle figure di almeno una commessa: puoi archiviarlo, non eliminarlo.");
        db.Attori.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<IncaricoDiAttore>> IncarichiDiAttoreAsync(Guid attoreId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await (from i in db.IncarichiCommessa.AsNoTracking()
                      join c in db.Commesse.AsNoTracking() on i.CommessaId equals c.Id
                      where i.AttoreId == attoreId
                      orderby c.Codice, i.Ruolo, i.Dal
                      select new IncaricoDiAttore(c.Id, c.Codice, c.Descrizione, i.Ruolo, i.Dal, i.Al)).ToListAsync(ct);
    }

    /// <summary>Tutti gli incarichi della commessa, in carica e conclusi, per figura e data.</summary>
    public async Task<IReadOnlyList<DatiIncarico>> IncarichiAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var oggi = Oggi();
        var elenco = await db.IncarichiCommessa.AsNoTracking().Include(i => i.Attore)
            .Where(i => i.CommessaId == commessaId).ToListAsync(ct);
        return elenco
            .OrderBy(i => i.Ruolo).ThenBy(i => InCarica(i, oggi) ? 0 : 1).ThenByDescending(i => i.Dal ?? DateOnly.MinValue).ThenBy(i => i.Attore!.Nominativo)
            .Select(i => new DatiIncarico(i.Id, i.Versione, i.CommessaId, i.Ruolo, i.AttoreId, i.Attore!.Nominativo, i.Attore.Titolo, i.Attore.Ente,
                i.Attore.Email, i.Attore.Pec, i.Attore.Telefono, i.Dal, i.Al, i.Note, InCarica(i, oggi), Formato.UltimaModifica(i)))
            .ToList();
    }

    /// <summary>
    /// Figure in carica alla data indicata (oggi se non indicata), con tutti i recapiti dell'attore:
    /// da usare per i destinatari delle comunicazioni della commessa.
    /// </summary>
    public async Task<IReadOnlyList<FiguraCommessa>> FigureInCaricaAsync(Guid commessaId, DateOnly? allaData = null, RuoloCommessa? ruolo = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var data = allaData ?? Oggi();
        var query = db.IncarichiCommessa.AsNoTracking().Include(i => i.Attore).Where(i => i.CommessaId == commessaId);
        if (ruolo is { } r) query = query.Where(i => i.Ruolo == r);
        var elenco = await query.ToListAsync(ct);
        return elenco.Where(i => InCarica(i, data))
            .OrderBy(i => i.Ruolo).ThenBy(i => i.Dal ?? DateOnly.MinValue).ThenBy(i => i.Attore!.Nominativo)
            .Select(i => new FiguraCommessa(i.Ruolo, i.AttoreId, i.Attore!.Nominativo, i.Attore.Titolo, i.Attore.Ente,
                i.Attore.Indirizzo, i.Attore.Cap, i.Attore.Comune, i.Attore.Provincia, i.Attore.Email, i.Attore.Pec, i.Attore.Telefono, i.Dal, i.Al))
            .ToList();
    }

    /// <summary>Aggiunge un soggetto a una figura (anche se la figura ne ha già altri).</summary>
    public async Task<DatiIncarico> AggiungiIncaricoAsync(Guid commessaId, RuoloCommessa ruolo, Guid attoreId, DateOnly? dal, string? note = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        if (!await db.Commesse.AnyAsync(c => c.Id == commessaId, ct))
            throw new OperazioneNonValidaException("La commessa non esiste.");
        var attore = await db.Attori.FirstOrDefaultAsync(a => a.Id == attoreId, ct)
            ?? throw new OperazioneNonValidaException("Scegli un attore dall'archivio.");
        await ControllaSovrapposizioneAsync(db, null, commessaId, ruolo, attoreId, dal, null, ct);

        var i = new IncaricoCommessa { CommessaId = commessaId, Ruolo = ruolo, AttoreId = attoreId, Dal = dal, Note = Pulisci(note) };
        db.IncarichiCommessa.Add(i);
        db.NotaStorico = $"{RuoliCommessa.Testo(ruolo)}: {attore.Nominativo}";
        await db.SaveChangesAsync(ct);
        return (await IncarichiAsync(commessaId, ct)).First(x => x.Id == i.Id);
    }

    /// <summary>Correzione di date e note di un incarico (es. data della nomina inserita male).</summary>
    public async Task<DatiIncarico> CorreggiIncaricoAsync(Guid id, int versione, DateOnly? dal, DateOnly? al, string? note, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var i = await CaricaIncaricoAsync(db, id, versione, ct);
        ControllaDate(dal, al);
        await ControllaSovrapposizioneAsync(db, i.Id, i.CommessaId, i.Ruolo, i.AttoreId, dal, al, ct);
        (i.Dal, i.Al, i.Note) = (dal, al, Pulisci(note));
        db.NotaStorico = $"{RuoliCommessa.Testo(i.Ruolo)}: correzione dell'incarico di {i.Attore!.Nominativo}";
        await db.SaveChangesAsync(ct);
        return (await IncarichiAsync(i.CommessaId, ct)).First(x => x.Id == i.Id);
    }

    /// <summary>Fine dell'incarico senza sostituto (es. figura non più prevista); l'incarico resta nello storico.</summary>
    public async Task TerminaIncaricoAsync(Guid id, int versione, DateOnly al, string? nota = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var i = await CaricaIncaricoAsync(db, id, versione, ct);
        if (i.Al is not null) throw new OperazioneNonValidaException("L'incarico è già concluso.");
        ControllaDate(i.Dal, al);
        i.Al = al;
        if (Pulisci(nota) is { } n) i.Note = string.IsNullOrEmpty(i.Note) ? n : $"{i.Note} — {n}";
        db.NotaStorico = $"{RuoliCommessa.Testo(i.Ruolo)}: fine incarico di {i.Attore!.Nominativo} il {Formato.Data(al)}";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cambio in corso d'opera: dalla data indicata subentra il nuovo attore. L'incarico precedente si chiude
    /// il giorno prima e resta nello storico della commessa.
    /// </summary>
    public async Task<DatiIncarico> SostituisciAsync(Guid id, int versione, Guid nuovoAttoreId, DateOnly dal, string? nota = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var vecchio = await CaricaIncaricoAsync(db, id, versione, ct);
        if (vecchio.Al is not null) throw new OperazioneNonValidaException("L'incarico è già concluso: aggiungi il nuovo soggetto alla figura.");
        if (vecchio.AttoreId == nuovoAttoreId) throw new OperazioneNonValidaException("Il nuovo soggetto è lo stesso già in carica.");
        if (vecchio.Dal is { } inizio && dal <= inizio)
            throw new OperazioneNonValidaException($"La data del subentro deve essere successiva all'inizio dell'incarico attuale ({Formato.Data(inizio)}).");
        var nuovoAttore = await db.Attori.FirstOrDefaultAsync(a => a.Id == nuovoAttoreId, ct)
            ?? throw new OperazioneNonValidaException("Scegli un attore dall'archivio.");
        await ControllaSovrapposizioneAsync(db, null, vecchio.CommessaId, vecchio.Ruolo, nuovoAttoreId, dal, null, ct);

        vecchio.Al = dal.AddDays(-1);
        var nuovo = new IncaricoCommessa { CommessaId = vecchio.CommessaId, Ruolo = vecchio.Ruolo, AttoreId = nuovoAttoreId, Dal = dal, Note = Pulisci(nota) };
        db.IncarichiCommessa.Add(nuovo);
        db.NotaStorico = $"{RuoliCommessa.Testo(vecchio.Ruolo)}: dal {Formato.Data(dal)} {nuovoAttore.Nominativo} subentra a {vecchio.Attore!.Nominativo}"
            + (Pulisci(nota) is { } n ? $" ({n})" : "");
        await db.SaveChangesAsync(ct);
        return (await IncarichiAsync(vecchio.CommessaId, ct)).First(x => x.Id == nuovo.Id);
    }

    /// <summary>Elimina un incarico inserito per errore (resta traccia nello storico). Per un cambio reale si usa la sostituzione.</summary>
    public async Task EliminaIncaricoAsync(Guid id, int versione, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var i = await CaricaIncaricoAsync(db, id, versione, ct);
        db.IncarichiCommessa.Remove(i);
        db.NotaStorico = $"{RuoliCommessa.Testo(i.Ruolo)}: eliminato l'incarico di {i.Attore!.Nominativo} (inserito per errore)";
        await db.SaveChangesAsync(ct);
    }

    private DateOnly Oggi() => DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);

    private static bool InCarica(IncaricoCommessa i, DateOnly data) => (i.Dal is null || i.Dal <= data) && (i.Al is null || i.Al >= data);

    private static async Task<IncaricoCommessa> CaricaIncaricoAsync(CoreDbContext db, Guid id, int versioneLetta, CancellationToken ct)
    {
        var i = await db.IncarichiCommessa.Include(x => x.Attore).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("L'incarico non esiste più: ricarica la pagina.");
        db.Entry(i).Property(x => x.Versione).OriginalValue = versioneLetta;
        return i;
    }

    private static void ControllaDate(DateOnly? dal, DateOnly? al)
    {
        if (dal is { } d && al is { } a && a < d)
            throw new OperazioneNonValidaException("La data di fine non può precedere la data di inizio.");
    }

    /// <summary>Lo stesso attore non può avere due incarichi sovrapposti nella stessa figura della stessa commessa.</summary>
    private static async Task ControllaSovrapposizioneAsync(CoreDbContext db, Guid? escluso, Guid commessaId, RuoloCommessa ruolo, Guid attoreId,
        DateOnly? dal, DateOnly? al, CancellationToken ct)
    {
        var altri = await db.IncarichiCommessa.AsNoTracking()
            .Where(i => i.CommessaId == commessaId && i.Ruolo == ruolo && i.AttoreId == attoreId && i.Id != escluso).ToListAsync(ct);
        var inizio = dal ?? DateOnly.MinValue;
        var fine = al ?? DateOnly.MaxValue;
        if (altri.Any(i => (i.Dal ?? DateOnly.MinValue) <= fine && (i.Al ?? DateOnly.MaxValue) >= inizio))
            throw new OperazioneNonValidaException($"Questo soggetto ricopre già la figura «{RuoliCommessa.Testo(ruolo)}» nello stesso periodo.");
    }

    private static void ControllaIndirizzo(string? indirizzo, string campo)
    {
        if (!string.IsNullOrWhiteSpace(indirizzo) && !IndirizzoEmail().IsMatch(indirizzo.Trim()))
            throw new OperazioneNonValidaException($"{campo} «{indirizzo.Trim()}» non sembra valido (esempio: nome@dominio.it).");
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex IndirizzoEmail();

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
