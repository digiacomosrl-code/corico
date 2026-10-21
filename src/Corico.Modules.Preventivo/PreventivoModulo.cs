using System.Reflection;
using Corico.Core.Configurazione;
using Corico.Core.Moduli;
using Corico.Modules.Preventivo.Interfaccia;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Modules.Preventivo;

/// <summary>Modulo 1 — Preventivo economico di commessa. Tabelle con prefisso PRV_.</summary>
public sealed class PreventivoModulo : ICoricoModulo
{
    public string Codice => "PRV";
    public string Nome => "Preventivo";
    public Assembly AssemblyInterfaccia => typeof(PreventivoModulo).Assembly;

    public IReadOnlyList<VoceMenu> VociMenu { get; } = [];

    public IReadOnlyList<SchedaCommessa> SchedeCommessa { get; } =
    [
        new("Preventivo", typeof(SchedaPreventivo), Ordine: 10),
    ];

    public void RegistraServizi(IServiceCollection servizi, PercorsiDati percorsi)
    {
    }

    public Task ApplicaMigrazioniAsync(IServiceProvider servizi, CancellationToken ct) => Task.CompletedTask;
}
