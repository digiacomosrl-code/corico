using Corico.Core.Backup;
using Corico.Core.Dati;
using Corico.Core.Moduli;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

/// <summary>Applica prima le migrazioni del Core, poi quelle di ciascun modulo attivo.</summary>
public sealed class MigrazioniDatabase(IServiceProvider servizi, IDbContextFactory<CoreDbContext> fabbrica, RegistroModuli moduli)
    : IMigrazioniDatabase
{
    public async Task ApplicaAsync(CancellationToken ct = default)
    {
        await using (var db = await fabbrica.CreateDbContextAsync(ct))
        {
            await db.Database.MigrateAsync(ct);
        }
        foreach (var modulo in moduli.Attivi)
            await modulo.ApplicaMigrazioniAsync(servizi, ct);
    }
}
