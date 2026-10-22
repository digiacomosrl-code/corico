using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Core.Dati;

/// <summary>
/// DbContext (del Core o di un modulo) le cui modifiche registrate nello storico si possono annullare.
/// Ogni modulo registra il proprio con <see cref="RegistrazioneContesti.AggiungiContestoAnnullabile{T}"/>.
/// </summary>
public interface IContestoAnnullabile
{
    Task<CoricoDbContextBase> CreaAsync(CancellationToken ct);
}

internal sealed class ContestoAnnullabile<T>(IDbContextFactory<T> fabbrica) : IContestoAnnullabile where T : CoricoDbContextBase
{
    public async Task<CoricoDbContextBase> CreaAsync(CancellationToken ct) => await fabbrica.CreateDbContextAsync(ct);
}

public static class RegistrazioneContesti
{
    public static IServiceCollection AggiungiContestoAnnullabile<T>(this IServiceCollection servizi) where T : CoricoDbContextBase =>
        servizi.AddSingleton<IContestoAnnullabile, ContestoAnnullabile<T>>();
}
