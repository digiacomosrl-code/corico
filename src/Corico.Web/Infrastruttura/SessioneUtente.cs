using System.Security.Claims;
using Corico.Core.Utenti;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Corico.Web.Infrastruttura;

/// <summary>
/// Per ogni interazione del circuito imposta l'utente corrente (permessi e storico), aggiorna l'ultima attività
/// (scadenza per inattività) e la presenza degli utenti collegati.
/// </summary>
public sealed class UtenteCircuitHandler(
    SessioneUtente sessione, AuthenticationStateProvider autenticazione, UtentiService utenti, PresenzaService presenza) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        await AggiornaIdentitaAsync();
        if (sessione.Identita is { } u) presenza.Collega(sessione.Id, u);
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        presenza.Scollega(sessione.Id);
        return Task.CompletedTask;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async contesto =>
        {
            sessione.UltimaAttivita = DateTimeOffset.UtcNow;
            await AggiornaIdentitaAsync();
            // Un utente disattivato resta collegato ma non può più salvare nulla.
            using (UtenteCorrente.Imposta(sessione.Identita))
                await next(contesto);
        };

    private async Task AggiornaIdentitaAsync()
    {
        var stato = await autenticazione.GetAuthenticationStateAsync();
        sessione.Identita = SessioneUtente.IdDa(stato.User) is { } id
            ? await utenti.IdentitaAsync(id) ?? new IdentitaUtente(id, "", stato.User.Identity?.Name ?? "", RuoloUtente.Consultazione, Attivo: false)
            : null;
    }
}
