namespace Corico.Core.Backup;

/// <summary>Applica le migrazioni del Core e di tutti i moduli attivi (all'avvio e dopo un ripristino).</summary>
public interface IMigrazioniDatabase
{
    Task ApplicaAsync(CancellationToken ct = default);
}
