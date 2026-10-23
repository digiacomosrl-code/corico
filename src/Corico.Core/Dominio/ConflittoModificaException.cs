namespace Corico.Core.Dominio;

/// <summary>Un campo che l'altro utente ha salvato con un valore diverso dal tuo.</summary>
public sealed record DifferenzaConflitto(string Campo, string TuoValore, string ValoreSalvato);

/// <summary>
/// Il record è stato modificato da un altro utente (o da un'altra scheda del browser) dopo essere stato aperto (sez. 9.1).
/// Riporta chi ha salvato, quando e i valori in conflitto; con <see cref="Accetta"/> si possono riapplicare le
/// proprie modifiche sopra quelle salvate, per scelta esplicita dell'utente.
/// </summary>
public sealed class ConflittoModificaException(string messaggio, Exception? inner = null)
    : Exception(messaggio, inner)
{
    private static readonly AsyncLocal<IReadOnlyDictionary<Guid, int>?> Accettate = new();

    public string? Entita { get; init; }
    public string? SalvatoDa { get; init; }
    public DateTime? SalvatoIlUtc { get; init; }
    public IReadOnlyList<DifferenzaConflitto> Differenze { get; init; } = [];

    /// <summary>Versione ora salvata di ciascun record in conflitto.</summary>
    public IReadOnlyDictionary<Guid, int> VersioniSalvate { get; init; } = new Dictionary<Guid, int>();

    /// <summary>Falso se i dati sono stati eliminati nel frattempo: si può solo ricaricare.</summary>
    public bool Riapplicabile => VersioniSalvate.Count > 0;

    /// <summary>
    /// Durante il blocco i salvataggi di questi record usano la versione ora salvata come riferimento:
    /// le proprie modifiche sostituiscono quelle dell'altro utente, che l'utente ha visto e scelto di sovrascrivere.
    /// </summary>
    public static IDisposable Accetta(ConflittoModificaException conflitto)
    {
        var precedente = Accettate.Value;
        var unione = new Dictionary<Guid, int>(precedente ?? new Dictionary<Guid, int>());
        foreach (var (id, versione) in conflitto.VersioniSalvate) unione[id] = versione;
        Accettate.Value = unione;
        return new Ripristino(() => Accettate.Value = precedente);
    }

    internal static int? VersioneAccettata(Guid id) =>
        Accettate.Value is { } a && a.TryGetValue(id, out var v) ? v : null;

    private sealed class Ripristino(Action azione) : IDisposable
    {
        public void Dispose() => azione();
    }
}

/// <summary>Errore di validazione da mostrare all'utente.</summary>
public sealed class OperazioneNonValidaException(string messaggio) : Exception(messaggio);
