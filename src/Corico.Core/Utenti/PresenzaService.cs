using System.Collections.Concurrent;

namespace Corico.Core.Utenti;

public sealed record Collegamento(Guid Sessione, Guid UtenteId, string Nome, DateTime DalUtc, Guid? CommessaId);

/// <summary>
/// Utenti collegati e commessa che stanno consultando (sez. 9.1): serve per l'avviso preventivo
/// «anche X sta lavorando su questa commessa», senza blocchi. Solo in memoria.
/// </summary>
public sealed class PresenzaService(TimeProvider orologio)
{
    private readonly ConcurrentDictionary<Guid, Collegamento> _sessioni = new();

    public event Action? Cambiato;

    public void Collega(Guid sessione, IdentitaUtente utente)
    {
        _sessioni[sessione] = new Collegamento(sessione, utente.Id, utente.Nome, orologio.GetUtcNow().UtcDateTime, null);
        Cambiato?.Invoke();
    }

    public void Scollega(Guid sessione)
    {
        if (_sessioni.TryRemove(sessione, out _)) Cambiato?.Invoke();
    }

    public void ApriCommessa(Guid sessione, Guid? commessaId)
    {
        if (!_sessioni.TryGetValue(sessione, out var c) || c.CommessaId == commessaId) return;
        _sessioni[sessione] = c with { CommessaId = commessaId };
        Cambiato?.Invoke();
    }

    public IReadOnlyList<Collegamento> Collegati =>
        _sessioni.Values.OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Altri utenti (diversi da quello indicato) che hanno aperto la commessa.</summary>
    public IReadOnlyList<string> AltriSullaCommessa(Guid commessaId, Guid utenteId) =>
        _sessioni.Values.Where(c => c.CommessaId == commessaId && c.UtenteId != utenteId)
            .Select(c => c.Nome).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();
}
