using Corico.Core.Configurazione;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Corico.Core.Backup;

/// <summary>
/// Backup automatici senza intervento dell'utente (sez. 6.2):
/// al primo avvio della giornata, ogni N ore di utilizzo effettivo e alla chiusura.
/// </summary>
public sealed class BackupAutomaticoService(
    BackupService backup,
    MonitorAttivita attivita,
    IOptions<CoricoOpzioni> opzioni,
    TimeProvider orologio,
    ILogger<BackupAutomaticoService> log) : BackgroundService
{
    private static readonly TimeSpan IntervalloControllo = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var ultimo = await backup.UltimoAsync(Dominio.TipoBackup.Locale, soloRiusciti: true, stoppingToken);
            var oggi = orologio.GetLocalNow().Date;
            if (ultimo is null || ultimo.DataOra.ToLocalTime().Date < oggi)
                await backup.EseguiBackupLocaleAsync(MotivoBackup.AvvioGiornaliero, stoppingToken);

            using var timer = new PeriodicTimer(IntervalloControllo, orologio);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ControllaBackupPeriodicoAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Errore nel servizio di backup automatico");
        }
    }

    private async Task ControllaBackupPeriodicoAsync(CancellationToken ct)
    {
        var ultimo = await backup.UltimoAsync(Dominio.TipoBackup.Locale, soloRiusciti: true, ct);
        var adesso = orologio.GetUtcNow().UtcDateTime;
        var ultimaData = ultimo?.DataOra ?? DateTime.MinValue;
        var usatoDopoUltimoBackup = attivita.UltimaAttivita > ultimaData;
        if (usatoDopoUltimoBackup && adesso - ultimaData >= TimeSpan.FromHours(opzioni.Value.OreTraBackupAutomatici))
            await backup.EseguiBackupLocaleAsync(MotivoBackup.Periodico, ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var ultimo = await backup.UltimoAsync(Dominio.TipoBackup.Locale, soloRiusciti: true, CancellationToken.None);
        if (ultimo is null || attivita.UltimaAttivita > ultimo.DataOra)
            await backup.EseguiBackupLocaleAsync(MotivoBackup.Chiusura, CancellationToken.None);
    }
}
