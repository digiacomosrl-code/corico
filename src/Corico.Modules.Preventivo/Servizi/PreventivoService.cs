using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record RigaVoce(
    Guid Id, int Versione, int Ordine, string? Tariffa, string Descrizione, string? Unita,
    decimal Quantita, decimal Prezzo, decimal PercentualeNonSoggetta,
    string? SuperCategoria, string? Categoria, string? CodiceWbs, bool SenzaElencoPrezzi, int RigheMisura, string? Note,
    RicavoVoce Ricavo);

public sealed record DatiComputo(Guid Id, string Descrizione, OrigineComputo Origine, string? NomeFile, Guid? DocumentoId,
    DateTime CreatoIl, decimal? TotaleImportato, decimal? TotaleDichiarato, string? Oggetto, string? Committente);

public sealed record StatoPreventivo(Guid PreventivoId, int Versione, decimal RibassoPercentuale, DatiComputo? Computo, QuadroEconomico Quadro)
{
    /// <summary>Budget approvato che blocca i prezzi contrattuali (es. «Budget 0 approvato»); null se modificabili.</summary>
    public string? BloccoBudget { get; init; }
}

public sealed class DatiVoce
{
    public Guid Id { get; set; }
    public int Versione { get; set; }
    public string? Tariffa { get; set; }
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }
    public decimal Quantita { get; set; }
    public decimal Prezzo { get; set; }
    public decimal PercentualeNonSoggetta { get; set; }
    public string? Note { get; set; }
}

/// <summary>Preventivo in bozza: ribasso, computo attivo, voci e quadro economico dei ricavi.</summary>
public sealed class PreventivoService(IDbContextFactory<PreventivoDbContext> fabbrica, CommesseService commesse)
{
    public async Task<StatoPreventivo> StatoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var preventivo = await OttieniOCreaAsync(db, commessaId, ct);
        var computo = await db.Computi.AsNoTracking().FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);

        var voci = computo is null ? [] : await db.VociComputo.AsNoTracking()
            .Where(v => v.ComputoId == computo.Id && !v.Archiviata)
            .Select(v => new { v.Quantita, v.Prezzo, v.PercentualeNonSoggetta }).ToListAsync(ct);
        var valoreContrattuale = (await commesse.OttieniAsync(commessaId, ct))?.ValoreContrattuale;

        var quadro = QuadroEconomico.Calcola(preventivo.RibassoPercentuale,
            voci.Select(v => (v.Quantita, v.Prezzo, v.PercentualeNonSoggetta)), valoreContrattuale);
        var approvato = BudgetService.Approvato(await db.Versioni.AsNoTracking()
            .Where(v => v.CommessaId == commessaId && v.StatoBudget == StatoBudget.Approvato).ToListAsync(ct));
        return new StatoPreventivo(preventivo.Id, preventivo.Versione, preventivo.RibassoPercentuale,
            computo is null ? null : Dati(computo), quadro)
        {
            BloccoBudget = approvato is null ? null : BudgetService.Etichetta(approvato),
        };
    }

    public async Task ImpostaRibassoAsync(Guid commessaId, int versioneLetta, decimal ribassoPercentuale, CancellationToken ct = default)
    {
        if (ribassoPercentuale is < 0 or >= 100)
            throw new OperazioneNonValidaException("Il ribasso deve essere compreso tra 0 e 100%.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await OttieniOCreaAsync(db, commessaId, ct);
        db.Entry(p).Property(x => x.Versione).OriginalValue = versioneLetta;
        p.RibassoPercentuale = ribassoPercentuale;
        db.NotaStorico = "Ribasso d'asta di commessa";
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RigaVoce>> VociAsync(Guid commessaId, string? testo = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var preventivo = await OttieniOCreaAsync(db, commessaId, ct);
        var computo = await db.Computi.AsNoTracking().FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);
        if (computo is null) return [];

        var categorie = await db.Categorie.AsNoTracking().Where(c => c.ComputoId == computo.Id)
            .ToDictionaryAsync(c => c.Id, c => c.Descrizione, ct);
        var query = db.VociComputo.AsNoTracking().Where(v => v.ComputoId == computo.Id && !v.Archiviata);
        if (!string.IsNullOrWhiteSpace(testo))
        {
            var t = testo.Trim().ToLower();
            query = query.Where(v => v.Descrizione.ToLower().Contains(t) || (v.Tariffa != null && v.Tariffa.ToLower().Contains(t)));
        }
        var voci = await query.OrderBy(v => v.Ordine)
            .Select(v => new { Voce = v, Misure = v.RigheMisura.Count }).ToListAsync(ct);

        return voci.Select(x =>
        {
            var v = x.Voce;
            return new RigaVoce(v.Id, v.Versione, v.Ordine, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo,
                v.PercentualeNonSoggetta,
                v.SuperCategoriaId is { } sc ? categorie.GetValueOrDefault(sc) : null,
                v.CategoriaId is { } c ? categorie.GetValueOrDefault(c) : null,
                v.CodiceWbs, v.VoceElencoPrezziId is null && computo.Origine != OrigineComputo.Manuale, x.Misure, v.Note,
                CalcoloRicavi.Voce(v.Quantita, v.Prezzo, preventivo.RibassoPercentuale, v.PercentualeNonSoggetta));
        }).ToList();
    }

    public async Task<IReadOnlyList<RigaMisura>> RigheMisuraAsync(Guid voceId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.RigheMisura.AsNoTracking().Where(r => r.VoceComputoId == voceId).OrderBy(r => r.Ordine).ToListAsync(ct);
    }

    /// <summary>Inserimento o modifica manuale di una voce. Se non c'è un computo attivo ne crea uno manuale.</summary>
    public async Task SalvaVoceAsync(Guid commessaId, DatiVoce dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Descrizione))
            throw new OperazioneNonValidaException("La descrizione della voce è obbligatoria.");
        if (dati.PercentualeNonSoggetta is < 0 or > 100)
            throw new OperazioneNonValidaException("La quota non soggetta a ribasso deve essere tra 0 e 100%.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        VoceComputo v;
        if (dati.Id == Guid.Empty)
        {
            var computo = await db.Computi.FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);
            if (computo is null)
            {
                var p = await OttieniOCreaAsync(db, commessaId, ct);
                computo = new Computo
                {
                    PreventivoId = p.Id, CommessaId = commessaId, Descrizione = "Computo inserito manualmente",
                    Origine = OrigineComputo.Manuale, Attivo = true,
                };
                db.Computi.Add(computo);
            }
            var ultimo = await db.VociComputo.Where(x => x.ComputoId == computo.Id).MaxAsync(x => (int?)x.Ordine, ct) ?? 0;
            v = new VoceComputo { ComputoId = computo.Id, CommessaId = commessaId, Ordine = ultimo + 1 };
            db.VociComputo.Add(v);
        }
        else
        {
            v = await db.VociComputo.FirstOrDefaultAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct)
                ?? throw new OperazioneNonValidaException("La voce non esiste.");
            db.Entry(v).Property(x => x.Versione).OriginalValue = dati.Versione;
        }

        v.Tariffa = string.IsNullOrWhiteSpace(dati.Tariffa) ? null : dati.Tariffa.Trim();
        v.Descrizione = dati.Descrizione.Trim();
        v.Unita = Importazione.UnitaMisura.Normalizza(dati.Unita);
        v.Quantita = dati.Quantita;
        v.Prezzo = dati.Prezzo;
        v.PercentualeNonSoggetta = dati.PercentualeNonSoggetta;
        v.Note = string.IsNullOrWhiteSpace(dati.Note) ? null : dati.Note.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiviaVoceAsync(Guid commessaId, Guid voceId, int versioneLetta, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var v = await db.VociComputo.FirstOrDefaultAsync(x => x.Id == voceId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La voce non esiste.");
        db.Entry(v).Property(x => x.Versione).OriginalValue = versioneLetta;
        v.Archiviata = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Imposta la quota non soggetta a ribasso su più voci insieme (es. tutta una categoria di sicurezza).</summary>
    public async Task<int> ImpostaNonSoggettaAsync(Guid commessaId, IReadOnlyCollection<Guid> voci, decimal percentuale, CancellationToken ct = default)
    {
        if (percentuale is < 0 or > 100)
            throw new OperazioneNonValidaException("La quota non soggetta a ribasso deve essere tra 0 e 100%.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var elenco = await db.VociComputo.Where(v => v.CommessaId == commessaId && voci.Contains(v.Id)).ToListAsync(ct);
        foreach (var v in elenco) v.PercentualeNonSoggetta = percentuale;
        db.NotaStorico = $"Quota non soggetta a ribasso impostata al {percentuale}% su {elenco.Count} voci";
        await db.SaveChangesAsync(ct);
        return elenco.Count;
    }

    public async Task<IReadOnlyList<DatiComputo>> ComputiAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var elenco = await db.Computi.AsNoTracking().Where(c => c.CommessaId == commessaId).ToListAsync(ct);
        return elenco.OrderByDescending(c => c.CreatoIl).Select(Dati).ToList();
    }

    public async Task ImpostaTotaleDichiaratoAsync(Guid commessaId, decimal? totale, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.Computi.FirstOrDefaultAsync(x => x.CommessaId == commessaId && x.Attivo, ct)
            ?? throw new OperazioneNonValidaException("Non c'è un computo attivo.");
        c.TotaleDichiarato = totale;
        await db.SaveChangesAsync(ct);
    }

    internal static async Task<Dominio.Preventivo> OttieniOCreaAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var p = await db.Preventivi.FirstOrDefaultAsync(x => x.CommessaId == commessaId, ct);
        if (p is not null) return p;
        p = new Dominio.Preventivo { CommessaId = commessaId };
        db.Preventivi.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    private static DatiComputo Dati(Computo c) => new(c.Id, c.Descrizione, c.Origine, c.NomeFile, c.DocumentoId,
        DateTime.SpecifyKind(c.CreatoIl, DateTimeKind.Utc), c.TotaleImportato, c.TotaleDichiarato, c.Oggetto, c.Committente);
}
