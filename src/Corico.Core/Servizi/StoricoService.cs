using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed class StoricoService(IDbContextFactory<CoreDbContext> fabbrica)
{
    public async Task<IReadOnlyList<VoceStorico>> PerCommessaAsync(Guid commessaId, int massimo = 500, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Storico.AsNoTracking().Where(s => s.CommessaId == commessaId)
            .OrderByDescending(s => s.Id).Take(massimo).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VoceStorico>> UltimeAsync(int massimo = 200, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Storico.AsNoTracking().OrderByDescending(s => s.Id).Take(massimo).ToListAsync(ct);
    }

    /// <summary>Ultima operazione registrata sulla commessa (dati generali, preventivo, pacchetti, ordini…).</summary>
    public async Task<VoceStorico?> UltimaAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Storico.AsNoTracking().Where(v => v.CommessaId == commessaId).OrderByDescending(v => v.Id).FirstOrDefaultAsync(ct);
    }
}
