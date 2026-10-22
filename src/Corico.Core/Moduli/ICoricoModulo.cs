using System.Reflection;
using Corico.Core.Configurazione;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Core.Moduli;

/// <summary>
/// Contratto che ogni modulo funzionale implementa per registrarsi nel Core (sez. 3.4).
/// Il Core non conosce i moduli: li riceve all'avvio tramite questa interfaccia.
/// </summary>
public interface ICoricoModulo
{
    /// <summary>Codice breve, usato anche come prefisso delle tabelle (es. "PRV").</summary>
    string Codice { get; }

    string Nome { get; }

    /// <summary>Assembly che contiene le pagine del modulo, aggiunto al router.</summary>
    Assembly AssemblyInterfaccia { get; }

    /// <summary>Voci del menu laterale.</summary>
    IReadOnlyList<VoceMenu> VociMenu { get; }

    /// <summary>Schede aggiuntive mostrate nella pagina della commessa.</summary>
    IReadOnlyList<SchedaCommessa> SchedeCommessa { get; }

    void RegistraServizi(IServiceCollection servizi, PercorsiDati percorsi);

    /// <summary>Applica le migrazioni del modulo (tabelle con il proprio prefisso).</summary>
    Task ApplicaMigrazioniAsync(IServiceProvider servizi, CancellationToken ct);

    /// <summary>Indirizzi HTTP del modulo (es. scaricamento di esportazioni). Facoltativo.</summary>
    void MappaIndirizzi(IEndpointRouteBuilder app) { }

    /// <summary>
    /// Controlli del modulo dopo un aggiornamento del programma (sez. 5.3), es. ricalcolo delle commesse
    /// di riferimento. Un controllo non superato provoca il ritorno alla versione precedente.
    /// </summary>
    Task<IReadOnlyList<EsitoControllo>> ControlliDopoAggiornamentoAsync(IServiceProvider servizi, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EsitoControllo>>([]);
}

public sealed record EsitoControllo(string Controllo, bool Superato, string Dettaglio);

public sealed record VoceMenu(string Titolo, string Indirizzo, int Ordine = 100);

/// <summary>
/// Scheda di un modulo dentro la pagina della commessa. Il componente riceve
/// il parametro "CommessaId" (Guid).
/// </summary>
public sealed record SchedaCommessa(string Titolo, Type Componente, int Ordine = 100);
