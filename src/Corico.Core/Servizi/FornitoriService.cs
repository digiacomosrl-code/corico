using Corico.Core.Calcolo;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed class DatiFornitore
{
    public Guid Id { get; set; }
    public int Versione { get; set; }
    public string Denominazione { get; set; } = "";
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? Indirizzo { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? Categorie { get; set; }
    public string? Note { get; set; }
    public bool Archiviato { get; set; }

    /// <summary>Solo lettura: chi ha modificato la scheda per ultimo e quando.</summary>
    public string UltimaModifica { get; set; } = "";

    public DatiFornitore Clona() => (DatiFornitore)MemberwiseClone();

    internal static DatiFornitore Da(Fornitore f) => new()
    {
        Id = f.Id, Versione = f.Versione, Denominazione = f.Denominazione, PartitaIva = f.PartitaIva,
        CodiceFiscale = f.CodiceFiscale, Indirizzo = f.Indirizzo, Comune = f.Comune, Provincia = f.Provincia,
        Email = f.Email, Telefono = f.Telefono, Categorie = f.Categorie, Note = f.Note, Archiviato = f.Archiviato,
        UltimaModifica = Formato.UltimaModifica(f),
    };
}

/// <summary>Anagrafica fornitori del Core (decisione del 22/10/2026).</summary>
public sealed class FornitoriService(IDbContextFactory<CoreDbContext> fabbrica)
{
    public async Task<IReadOnlyList<DatiFornitore>> ElencoAsync(string? testo = null, bool includiArchiviati = false, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var query = db.Fornitori.AsNoTracking().AsQueryable();
        if (!includiArchiviati) query = query.Where(f => !f.Archiviato);
        if (!string.IsNullOrWhiteSpace(testo))
        {
            var t = testo.Trim().ToLower();
            query = query.Where(f => f.Denominazione.ToLower().Contains(t)
                || (f.PartitaIva != null && f.PartitaIva.Contains(t))
                || (f.Categorie != null && f.Categorie.ToLower().Contains(t)));
        }
        var elenco = await query.OrderBy(f => f.Denominazione).ToListAsync(ct);
        return elenco.Select(DatiFornitore.Da).ToList();
    }

    public async Task<DatiFornitore?> OttieniAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var f = await db.Fornitori.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return f is null ? null : DatiFornitore.Da(f);
    }

    public async Task<IReadOnlyList<PossibileDuplicato>> PossibiliDuplicatiAsync(DatiFornitore dati, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var altri = await db.Fornitori.AsNoTracking().Where(f => f.Id != dati.Id)
            .Select(f => new { f.Id, f.Denominazione, f.PartitaIva, f.CodiceFiscale }).ToListAsync(ct);
        return ControlloDuplicati.Cerca(dati.Denominazione, dati.PartitaIva, dati.CodiceFiscale,
            altri.Select(f => (f.Id, f.Denominazione, f.PartitaIva, f.CodiceFiscale)));
    }

    public async Task<DatiFornitore> SalvaAsync(DatiFornitore dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Denominazione))
            throw new OperazioneNonValidaException("La denominazione del fornitore è obbligatoria.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        Fornitore f;
        if (dati.Id == Guid.Empty)
        {
            f = new Fornitore();
            db.Fornitori.Add(f);
        }
        else
        {
            f = await db.Fornitori.FirstOrDefaultAsync(x => x.Id == dati.Id, ct)
                ?? throw new OperazioneNonValidaException("Il fornitore non esiste.");
            db.Entry(f).Property(x => x.Versione).OriginalValue = dati.Versione;
        }

        f.Denominazione = dati.Denominazione.Trim();
        f.PartitaIva = Pulisci(dati.PartitaIva);
        f.CodiceFiscale = Pulisci(dati.CodiceFiscale)?.ToUpperInvariant();
        f.Indirizzo = Pulisci(dati.Indirizzo);
        f.Comune = Pulisci(dati.Comune);
        f.Provincia = Pulisci(dati.Provincia)?.ToUpperInvariant();
        f.Email = Pulisci(dati.Email);
        f.Telefono = Pulisci(dati.Telefono);
        f.Categorie = Pulisci(dati.Categorie);
        f.Note = Pulisci(dati.Note);

        await db.SaveChangesAsync(ct);
        return DatiFornitore.Da(f);
    }

    public async Task<DatiFornitore> ImpostaArchiviazioneAsync(Guid id, int versione, bool archiviato, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var f = await db.Fornitori.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("Il fornitore non esiste.");
        db.Entry(f).Property(x => x.Versione).OriginalValue = versione;
        f.Archiviato = archiviato;
        await db.SaveChangesAsync(ct);
        return DatiFornitore.Da(f);
    }

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
