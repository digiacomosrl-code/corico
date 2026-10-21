using System.Text;
using Corico.Core.Backup;
using Corico.Core.Dominio;
using Corico.Core.Servizi;

namespace Corico.Core.Tests;

public class BackupTests
{
    [Fact]
    public async Task Backup_verificato_e_ripristino_reversibile()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var backup = amb.Get<BackupService>();

        await commesse.CreaAsync("Prima della modifica");
        var esito = await backup.EseguiBackupLocaleAsync(MotivoBackup.Manuale);
        Assert.True(esito.Riuscito, esito.Messaggio);
        Assert.False(File.Exists(esito.Percorso + "-wal"));

        amb.Orologio.Advance(TimeSpan.FromMinutes(5));
        await commesse.CreaAsync("Creata dopo il backup");
        Assert.Equal(2, (await commesse.ElencoAsync()).Count);

        var ripristino = await backup.RipristinaAsync(esito.Percorso!);
        Assert.True(ripristino.Riuscito, ripristino.Messaggio);
        Assert.Single(await commesse.ElencoAsync());

        // Il codice 2026-002, già assegnato prima del ripristino, non viene riutilizzato.
        Assert.Equal("2026-003", (await commesse.CreaAsync("Dopo il ripristino")).Codice);

        // Lo stato precedente al ripristino è stato salvato e contiene entrambe le commesse.
        var statoPrecedente = backup.ElencoBackupLocali().Single(b => b.Motivo == nameof(MotivoBackup.PrimaDiRipristino));
        amb.Orologio.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await backup.RipristinaAsync(statoPrecedente.Percorso)).Riuscito);
        Assert.Equal(2, (await commesse.ElencoAsync()).Count);
        Assert.Equal("2026-004", (await commesse.CreaAsync("Nuova")).Codice);
    }

    [Fact]
    public async Task Backup_danneggiato_non_viene_ripristinato()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var finto = Path.Combine(amb.Percorsi.BackupDatabase, "corico_20261020_100000_Manuale.db");
        await File.WriteAllTextAsync(finto, "non è un database");

        var esito = await amb.Get<BackupService>().RipristinaAsync(finto);
        Assert.False(esito.Riuscito);
    }

    [Fact]
    public async Task Backup_esterno_su_disco_registrato_con_documenti_verificati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var backup = amb.Get<BackupService>();
        await amb.Get<DocumentiService>().CaricaAsync(new MemoryStream(Encoding.UTF8.GetBytes("doc")), "a.txt", "text/plain", null);

        var disco = Path.Combine(amb.Percorsi.Radice, "DiscoUSB");
        Directory.CreateDirectory(disco);

        Assert.False((await backup.EseguiBackupEsternoAsync(disco)).Riuscito); // disco non registrato

        await backup.RegistraDiscoAsync(disco, "Disco A");
        var esito = await backup.EseguiBackupEsternoAsync(disco);
        Assert.True(esito.Riuscito, esito.Messaggio);
        Assert.Equal(1, esito.DocumentiCopiati);
        Assert.NotNull(await backup.UltimoBackupEsternoAsync());

        Assert.Empty(await amb.Get<AvvisiService>().AvvisiDiSistemaAsync());
        amb.Orologio.Advance(TimeSpan.FromDays(8));
        Assert.Contains(await amb.Get<AvvisiService>().AvvisiDiSistemaAsync(), a => a.Gravita == GravitaAvviso.Attenzione);
    }
}
