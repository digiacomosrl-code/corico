using Corico.Core.Dominio;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record RiepilogoAnalisi(Guid Id, string? Codice, string Descrizione, string? Unita, StatoAnalisi Stato,
    int Risorse, decimal CostoUnitario, DateTime? ModificataIl);

/// <summary>
/// Analisi prezzi aziendali (sez. 15): create a mano o dalle risorse di una voce, confermate e poi riutilizzate.
/// Applicare un'analisi copia le risorse nella voce: le modifiche successive all'analisi non cambiano i preventivi.
/// </summary>
public sealed class AnalisiService(IDbContextFactory<PreventivoDbContext> fabbrica)
{
    public async Task<IReadOnlyList<RiepilogoAnalisi>> ElencoAsync(string? testo = null, bool soloConfermate = false,
        bool includiArchiviate = false, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = db.AnalisiAziendali.AsNoTracking().Include(a => a.Righe).AsQueryable();
        if (soloConfermate) q = q.Where(a => a.Stato == StatoAnalisi.Confermata);
        else if (!includiArchiviate) q = q.Where(a => a.Stato != StatoAnalisi.Archiviata);
        if (!string.IsNullOrWhiteSpace(testo))
        {
            var t = testo.Trim().ToLower();
            q = q.Where(a => a.Descrizione.ToLower().Contains(t) || (a.Codice != null && a.Codice.ToLower().Contains(t)));
        }
        var elenco = await q.ToListAsync(ct);
        return elenco.OrderBy(a => a.Codice ?? "").ThenBy(a => a.Descrizione)
            .Select(a => new RiepilogoAnalisi(a.Id, a.Codice, a.Descrizione, a.Unita, a.Stato, a.Righe.Count,
                a.Righe.Sum(Risorse.CostoUnitario), a.ModificatoIl ?? a.CreatoIl))
            .ToList();
    }

    public async Task<AnalisiAziendale?> OttieniAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var a = await db.AnalisiAziendali.AsNoTracking().Include(x => x.Righe).FirstOrDefaultAsync(x => x.Id == id, ct);
        a?.Righe.Sort((x, y) => x.Ordine.CompareTo(y.Ordine));
        return a;
    }

    public async Task<Guid> SalvaAsync(AnalisiAziendale dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Descrizione))
            throw new OperazioneNonValidaException("La descrizione dell'analisi è obbligatoria.");
        if (dati.Righe.Any(r => string.IsNullOrWhiteSpace(r.Descrizione)))
            throw new OperazioneNonValidaException("Ogni risorsa dell'analisi deve avere una descrizione.");
        if (dati.Righe.Any(r => r.Fabbisogno < 0 || r.Prezzo < 0 || r.SfridoPercentuale < 0))
            throw new OperazioneNonValidaException("Fabbisogno, sfrido e prezzo non possono essere negativi.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        AnalisiAziendale a;
        if (dati.Id == Guid.Empty || !await db.AnalisiAziendali.AnyAsync(x => x.Id == dati.Id, ct))
        {
            a = new AnalisiAziendale { Stato = StatoAnalisi.Bozza };
            db.AnalisiAziendali.Add(a);
        }
        else
        {
            a = await db.AnalisiAziendali.Include(x => x.Righe).FirstAsync(x => x.Id == dati.Id, ct);
            db.Entry(a).Property(x => x.Versione).OriginalValue = dati.Versione;
            db.RigheAnalisiAziendali.RemoveRange(a.Righe);
            a.Righe.Clear();
        }
        a.Codice = Pulisci(dati.Codice);
        a.Descrizione = dati.Descrizione.Trim();
        a.Unita = Importazione.UnitaMisura.Normalizza(dati.Unita);
        a.Fonte = Pulisci(dati.Fonte);
        a.Note = Pulisci(dati.Note);
        var ordine = 0;
        foreach (var r in dati.Righe)
        {
            var riga = new RigaAnalisiAziendale
            {
                AnalisiId = a.Id, Ordine = ++ordine, Categoria = r.Categoria, Descrizione = r.Descrizione.Trim(),
                Unita = Importazione.UnitaMisura.Normalizza(r.Unita), Fabbisogno = r.Fabbisogno,
                SfridoPercentuale = r.SfridoPercentuale, Prezzo = r.Prezzo, Fonte = Pulisci(r.Fonte), Note = Pulisci(r.Note),
            };
            a.Righe.Add(riga);
            db.RigheAnalisiAziendali.Add(riga);
        }
        await db.SaveChangesAsync(ct);
        return a.Id;
    }

    public async Task CambiaStatoAsync(Guid id, StatoAnalisi stato, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var a = await db.AnalisiAziendali.Include(x => x.Righe).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("L'analisi non esiste.");
        if (stato == StatoAnalisi.Confermata && a.Righe.Count == 0)
            throw new OperazioneNonValidaException("Un'analisi senza risorse non può essere confermata.");
        a.Stato = stato;
        db.NotaStorico = stato switch
        {
            StatoAnalisi.Confermata => "Analisi confermata: disponibile per tutte le commesse",
            StatoAnalisi.Archiviata => "Analisi archiviata",
            _ => "Analisi riportata in bozza",
        };
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Crea un'analisi in bozza dalle risorse di una voce (riutilizzo di analisi storiche delle commesse).</summary>
    public async Task<Guid> CreaDaRisorseAsync(string descrizione, string? unita, IEnumerable<RisorsaVoce> risorse, string? fonte,
        CancellationToken ct = default)
    {
        var a = new AnalisiAziendale { Id = Guid.Empty, Descrizione = descrizione, Unita = unita, Fonte = fonte };
        foreach (var r in risorse)
            a.Righe.Add(new RigaAnalisiAziendale
            {
                Categoria = r.Categoria, Descrizione = r.Descrizione, Unita = r.Unita, Fabbisogno = r.Fabbisogno,
                SfridoPercentuale = r.SfridoPercentuale, Prezzo = r.Prezzo, Fonte = r.Fonte, Note = r.Note,
            });
        return await SalvaAsync(a, ct);
    }

    /// <summary>Copia le risorse di un'analisi confermata, pronte da aggiungere a una voce d'offerta.</summary>
    public async Task<IReadOnlyList<RisorsaVoce>> RisorseDaApplicareAsync(Guid analisiId, Guid commessaId, CancellationToken ct = default)
    {
        var a = await OttieniAsync(analisiId, ct) ?? throw new OperazioneNonValidaException("L'analisi non esiste.");
        if (a.Stato != StatoAnalisi.Confermata)
            throw new OperazioneNonValidaException("Si possono applicare solo analisi confermate.");
        var fonte = $"Analisi {a.Codice ?? a.Descrizione}";
        return a.Righe.Select(r => new RisorsaVoce
        {
            Id = Guid.Empty, CommessaId = commessaId, Categoria = r.Categoria, Descrizione = r.Descrizione, Unita = r.Unita,
            Fabbisogno = r.Fabbisogno, SfridoPercentuale = r.SfridoPercentuale, Prezzo = r.Prezzo,
            Fonte = r.Fonte ?? fonte, Note = r.Note, AnalisiId = a.Id,
        }).ToList();
    }

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
