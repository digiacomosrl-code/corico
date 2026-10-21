namespace Corico.Core.Dominio;

/// <summary>
/// Il record è stato modificato da un'altra scheda del browser dopo essere stato aperto.
/// </summary>
public sealed class ConflittoModificaException(string messaggio, Exception? inner = null)
    : Exception(messaggio, inner);

/// <summary>Errore di validazione da mostrare all'utente.</summary>
public sealed class OperazioneNonValidaException(string messaggio) : Exception(messaggio);
