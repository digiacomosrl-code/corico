using System.Security.Claims;

namespace Corico.Core.Utenti;

/// <summary>Sessione di lavoro (circuito Blazor) dell'utente collegato: identità aggiornata e ultima attività.</summary>
public sealed class SessioneUtente
{
    public Guid Id { get; } = Guid.NewGuid();
    public IdentitaUtente? Identita { get; set; }
    public DateTimeOffset UltimaAttivita { get; set; } = DateTimeOffset.UtcNow;

    public bool Amministratore => Identita?.Ruolo == RuoloUtente.Amministratore;
    public bool SolaConsultazione => Identita?.Ruolo == RuoloUtente.Consultazione;

    public static Guid? IdDa(ClaimsPrincipal utente) =>
        Guid.TryParse(utente.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
