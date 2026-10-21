using Corico.Core.Backup;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Corico.Web.Infrastruttura;

/// <summary>Registra ogni interazione dell'utente come "utilizzo effettivo" per i backup periodici.</summary>
public sealed class RegistraAttivitaCircuitHandler(MonitorAttivita attivita) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        contesto =>
        {
            attivita.Registra();
            return next(contesto);
        };
}
