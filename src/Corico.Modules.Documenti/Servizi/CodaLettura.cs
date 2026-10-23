using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corico.Modules.Documenti.Servizi;

/// <summary>
/// Coda in sottofondo: ogni pochi secondi legge i documenti caricati e non ancora letti, uno alla volta.
/// Il Core non conosce il modulo, quindi la coda guarda l'archivio invece di ricevere avvisi dal caricamento.
/// </summary>
public sealed class CodaLettura(LetturaArchivioService lettura, ILogger<CodaLettura> log) : BackgroundService
{
    private static readonly TimeSpan Intervallo = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await lettura.RicostruisciIndiceSeVuotoAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Ricostruzione dell'indice di ricerca non riuscita"); }

        while (!ct.IsCancellationRequested)
        {
            try { await lettura.LeggiInCodaAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Lettura dei documenti in coda non riuscita"); }
            try { await Task.Delay(Intervallo, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
