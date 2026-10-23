namespace Corico.Core.Configurazione;

/// <summary>
/// Opzioni lette dalla sezione "Corico" di appsettings.json.
/// </summary>
public sealed class CoricoOpzioni
{
    public const string Sezione = "Corico";

    /// <summary>Cartella radice dei dati (es. D:\CORICO). Separata dalla cartella del programma.</summary>
    public string CartellaDati { get; set; } = "";

    /// <summary>Porta su cui l'applicazione ascolta, solo su 127.0.0.1.</summary>
    public int Porta { get; set; } = 5080;

    /// <summary>Minuti di inattività dopo i quali la sessione scade e serve un nuovo accesso.</summary>
    public int MinutiInattivita { get; set; } = 60;

    /// <summary>Intervallo dei backup automatici durante l'utilizzo effettivo.</summary>
    public int OreTraBackupAutomatici { get; set; } = 2;

    /// <summary>Giorni dopo i quali si segnala il backup esterno mancante.</summary>
    public int GiorniAvvisoBackupEsterno { get; set; } = 7;

    /// <summary>Cartella del repository Git locale, copiata nel backup esterno (facoltativa).</summary>
    public string? CartellaCodiceSorgente { get; set; }

    /// <summary>Moduli funzionali attivi, per codice (es. "PRV": true).</summary>
    public Dictionary<string, bool> Moduli { get; set; } = new();
}
