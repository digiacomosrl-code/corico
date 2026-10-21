namespace Corico.Core.Utenti;

/// <summary>
/// Utente che esegue le operazioni. Oggi è sempre l'utente Windows del PC;
/// in futuro potrà provenire da un'autenticazione multiutente (sez. 3.5).
/// </summary>
public interface IUtenteCorrente
{
    string Nome { get; }
}

public sealed class UtenteWindows : IUtenteCorrente
{
    public string Nome { get; } = Environment.UserName;
}
