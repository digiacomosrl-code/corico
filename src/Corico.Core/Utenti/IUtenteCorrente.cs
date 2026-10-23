namespace Corico.Core.Utenti;

/// <summary>Utente collegato che esegue le operazioni (sez. 3.5).</summary>
public interface IUtenteCorrente
{
    /// <summary>Nome registrato nello storico e nei campi «creato da» / «modificato da».</summary>
    string Nome { get; }

    /// <summary>Utente autenticato; null per le operazioni di sistema (backup automatici, aggiornamento, prove).</summary>
    IdentitaUtente? Identita { get; }
}

/// <summary>Dati dell'utente collegato, validi per la durata di un'operazione.</summary>
public sealed record IdentitaUtente(Guid Id, string NomeUtente, string Nome, RuoloUtente Ruolo, bool Attivo = true, bool CambioPasswordObbligatorio = false,
    bool PuoEmettereOrdini = false)
{
    /// <summary>Amministratore oppure operatore con la spunta «Può emettere ordini e adottare variazioni».</summary>
    public bool AbilitatoOrdini => Attivo && (Ruolo == RuoloUtente.Amministratore || (Ruolo == RuoloUtente.Operatore && PuoEmettereOrdini));
}

/// <summary>
/// Utente corrente «di ambiente»: l'interfaccia lo imposta all'inizio di ogni interazione (circuito Blazor o richiesta HTTP)
/// e il valore segue il flusso asincrono dell'operazione. Senza utente impostato le operazioni sono di sistema
/// e si registrano con il nome dell'utente Windows del PC.
/// </summary>
public sealed class UtenteCorrente : IUtenteCorrente
{
    private static readonly AsyncLocal<IdentitaUtente?> Corrente = new();

    public string Nome => Corrente.Value?.Nome ?? Environment.UserName;

    public IdentitaUtente? Identita => Corrente.Value;

    /// <summary>Vero per l'amministratore e per le operazioni di sistema (nessun utente collegato).</summary>
    public static bool PuoAmministrare => Corrente.Value is null || Corrente.Value.Ruolo == RuoloUtente.Amministratore;

    /// <summary>Vero per chi può emettere ordini e adottare variazioni, e per le operazioni di sistema.</summary>
    public static bool PuoEmettereOrdini => Corrente.Value is null || Corrente.Value.AbilitatoOrdini;

    /// <summary>Imposta l'utente per l'operazione in corso; il valore precedente torna al termine (Dispose).</summary>
    public static IDisposable Imposta(IdentitaUtente? identita)
    {
        var precedente = Corrente.Value;
        Corrente.Value = identita;
        return new Ripristino(precedente);
    }

    private sealed class Ripristino(IdentitaUtente? precedente) : IDisposable
    {
        public void Dispose() => Corrente.Value = precedente;
    }
}
