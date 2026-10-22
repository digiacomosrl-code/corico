namespace Corico.Core.Backup;

/// <summary>Ultimo momento di utilizzo effettivo dell'applicazione, per i backup "ogni 2 ore di utilizzo".</summary>
public sealed class MonitorAttivita(TimeProvider orologio)
{
    private long _ultimaAttivitaTicks = orologio.GetUtcNow().UtcTicks;

    public DateTime UltimaAttivita => new(Interlocked.Read(ref _ultimaAttivitaTicks), DateTimeKind.Utc);

    public void Registra() => Interlocked.Exchange(ref _ultimaAttivitaTicks, orologio.GetUtcNow().UtcTicks);
}
