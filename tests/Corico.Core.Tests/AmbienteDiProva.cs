using Corico.Core;
using Corico.Core.Backup;
using Corico.Core.Configurazione;
using Corico.Core.Moduli;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Corico.Core.Tests;

/// <summary>Installazione completa del Core in una cartella temporanea, con orologio controllabile.</summary>
public sealed class AmbienteDiProva : IAsyncDisposable
{
    public PercorsiDati Percorsi { get; }
    public FakeTimeProvider Orologio { get; } = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Servizi { get; }

    private AmbienteDiProva(IEnumerable<ICoricoModulo> moduli, Action<IServiceCollection>? configura = null)
    {
        Percorsi = new PercorsiDati(Path.Combine(Path.GetTempPath(), "corico-test-" + Guid.NewGuid().ToString("N")));
        Percorsi.CreaCartelle();
        Orologio.SetLocalTimeZone(TimeZoneInfo.Utc);

        var servizi = new ServiceCollection();
        servizi.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        servizi.AddOptions<CoricoOpzioni>();
        servizi.AggiungiCorico(Percorsi, moduli);
        servizi.AddSingleton<TimeProvider>(Orologio);
        configura?.Invoke(servizi);
        Servizi = servizi.BuildServiceProvider();
    }

    public static Task<AmbienteDiProva> CreaAsync(params ICoricoModulo[] moduli) => CreaAsync(null, moduli);

    /// <summary>Con <paramref name="configura"/> si sostituiscono servizi esterni (es. il collegamento all'AI) con versioni simulate.</summary>
    public static async Task<AmbienteDiProva> CreaAsync(Action<IServiceCollection>? configura, params ICoricoModulo[] moduli)
    {
        var ambiente = new AmbienteDiProva(moduli, configura);
        await ambiente.Servizi.GetRequiredService<IMigrazioniDatabase>().ApplicaAsync();
        return ambiente;
    }

    public T Get<T>() where T : notnull => Servizi.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Servizi.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Percorsi.Radice, recursive: true); } catch (IOException) { }
    }
}
