using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed class ImpostazioniService(IDbContextFactory<CoreDbContext> fabbrica, TimeProvider orologio)
{
    public const string UltimoBackupEsterno = "Backup.UltimoBackupEsterno";
    public const string DischiEsterni = "Backup.DischiEsterni";

    public async Task<string?> LeggiAsync(string chiave, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return (await db.Impostazioni.AsNoTracking().FirstOrDefaultAsync(i => i.Chiave == chiave, ct))?.Valore;
    }

    public async Task ScriviAsync(string chiave, string? valore, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var imp = await db.Impostazioni.FirstOrDefaultAsync(i => i.Chiave == chiave, ct);
        if (imp is null)
        {
            imp = new Impostazione { Chiave = chiave };
            db.Impostazioni.Add(imp);
        }
        imp.Valore = valore;
        imp.ModificatoIl = orologio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
