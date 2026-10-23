using Corico.Core.Dominio;

namespace Corico.Core.Utenti;

/// <summary>
/// Conflitto di salvataggio da mostrare all'utente della sessione (sez. 9.1): i suoi valori accanto a quelli salvati
/// dall'altro utente, con la scelta tra ricaricare i dati salvati e riapplicare le proprie modifiche.
/// Il pannello che lo mostra è unico per tutta l'applicazione.
/// </summary>
public sealed class ConflittiService
{
    private Func<Task>? _riapplica, _ricarica;

    public ConflittoModificaException? Corrente { get; private set; }
    public string? Errore { get; private set; }
    public bool PuoRiapplicare => Corrente?.Riapplicabile == true && _riapplica is not null;

    public event Action? Cambiato;

    /// <param name="riapplica">La stessa operazione di salvataggio, da ripetere sulla versione ora salvata.</param>
    /// <param name="ricarica">Ricarica i dati mostrati nella pagina (anche dopo aver riapplicato).</param>
    public void Mostra(ConflittoModificaException conflitto, Func<Task>? riapplica, Func<Task>? ricarica)
    {
        (Corrente, _riapplica, _ricarica, Errore) = (conflitto, riapplica, ricarica, null);
        Cambiato?.Invoke();
    }

    public async Task RicaricaAsync()
    {
        var ricarica = _ricarica;
        Chiudi();
        if (ricarica is not null) await ricarica();
    }

    public async Task RiapplicaAsync()
    {
        if (Corrente is not { } conflitto || _riapplica is not { } riapplica) return;
        var ricarica = _ricarica;
        try
        {
            using (ConflittoModificaException.Accetta(conflitto))
                await riapplica();
            // La pagina può aver già mostrato un nuovo conflitto (su un altro record): in quel caso resta aperto.
            if (!ReferenceEquals(Corrente, conflitto)) return;
            Chiudi();
        }
        catch (ConflittoModificaException nuovo)
        {
            // Un altro record dell'operazione era cambiato: si mostra il nuovo conflitto.
            Mostra(nuovo, riapplica, ricarica);
            return;
        }
        catch (OperazioneNonValidaException ex)
        {
            Errore = ex.Message;
            Cambiato?.Invoke();
            return;
        }
        if (ricarica is not null) await ricarica();
    }

    public void Chiudi()
    {
        (Corrente, _riapplica, _ricarica, Errore) = (null, null, null, null);
        Cambiato?.Invoke();
    }
}
