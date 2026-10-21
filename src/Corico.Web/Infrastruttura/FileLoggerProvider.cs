using System.Collections.Concurrent;
using System.Text;

namespace Corico.Web.Infrastruttura;

/// <summary>
/// Registro tecnico giornaliero nella cartella Log (un file al giorno, conservati 90 giorni).
/// Implementazione minima per evitare dipendenze esterne.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _cartella;
    private readonly BlockingCollection<string> _coda = new(10_000);
    private readonly Thread _scrittore;

    public FileLoggerProvider(string cartella)
    {
        _cartella = cartella;
        Directory.CreateDirectory(cartella);
        PulisciVecchi();
        _scrittore = new Thread(Scrivi) { IsBackground = true, Name = "Corico.Log" };
        _scrittore.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Accoda(string riga) => _coda.TryAdd(riga);

    private void Scrivi()
    {
        foreach (var riga in _coda.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(Path.Combine(_cartella, $"corico-{DateTime.Now:yyyyMMdd}.log"), riga, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }
    }

    private void PulisciVecchi()
    {
        foreach (var file in Directory.EnumerateFiles(_cartella, "corico-*.log"))
            if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-90))
                try { File.Delete(file); } catch (IOException) { }
    }

    public void Dispose()
    {
        _coda.CompleteAdding();
        _scrittore.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(string categoria, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= (categoria.StartsWith("Corico", StringComparison.Ordinal) ? LogLevel.Information : LogLevel.Warning);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(logLevel).Append("] ")
                .Append(categoria).Append(": ").AppendLine(formatter(state, exception));
            if (exception is not null) sb.AppendLine(exception.ToString());
            provider.Accoda(sb.ToString());
        }
    }
}
