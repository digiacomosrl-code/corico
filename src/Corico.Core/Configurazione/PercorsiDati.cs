namespace Corico.Core.Configurazione;

/// <summary>
/// Struttura delle cartelle dati sul PC (prompt strategico, sez. 3.3).
/// </summary>
public sealed class PercorsiDati
{
    public PercorsiDati(string radice)
    {
        if (string.IsNullOrWhiteSpace(radice))
            throw new ArgumentException("La cartella dati non è configurata.", nameof(radice));

        Radice = Path.GetFullPath(radice);
    }

    public string Radice { get; }
    public string Dati => Path.Combine(Radice, "Dati");
    public string Documenti => Path.Combine(Radice, "Documenti");
    public string Backup => Path.Combine(Radice, "Backup");
    public string BackupDatabase => Path.Combine(Backup, "Database");
    public string BackupDocumenti => Path.Combine(Backup, "Documenti");
    public string Log => Path.Combine(Radice, "Log");
    public string Config => Path.Combine(Radice, "Config");
    public string FileDatabase => Path.Combine(Dati, "corico.db");

    public string StringaConnessione => $"Data Source={FileDatabase};Foreign Keys=True";

    public void CreaCartelle()
    {
        foreach (var cartella in new[] { Dati, Documenti, BackupDatabase, BackupDocumenti, Log, Config })
            Directory.CreateDirectory(cartella);
    }
}
