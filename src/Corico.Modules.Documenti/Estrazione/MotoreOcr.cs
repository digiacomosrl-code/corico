using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Corico.Modules.Documenti.Estrazione;

/// <summary>Riconoscimento del testo da un'immagine, sempre sul PC sede.</summary>
public interface IMotoreOcr
{
    /// <summary>False se il programma di OCR non è installato: le scansioni restano senza testo, con un avviso.</summary>
    bool Disponibile { get; }

    Task<string> RiconosciAsync(byte[] immaginePng, CancellationToken ct);
}

/// <summary>
/// OCR con Tesseract (licenza Apache 2.0) e il modello di lingua italiana, eseguito come programma separato.
/// Il programma si cerca, nell'ordine: impostazione "Corico:Ocr:Percorso", cartella "ocr" accanto a Corico, PATH di sistema.
/// </summary>
public sealed class OcrTesseract : IMotoreOcr
{
    private readonly string? _eseguibile;
    private readonly string? _cartellaLingue;
    private readonly ILogger<OcrTesseract>? _log;

    public OcrTesseract(IConfiguration? configurazione = null, ILogger<OcrTesseract>? log = null)
    {
        _log = log;
        var nome = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var candidati = new[]
        {
            configurazione?["Corico:Ocr:Percorso"],
            Path.Combine(AppContext.BaseDirectory, "ocr", nome),
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(c => Path.Combine(c, nome)));
        _eseguibile = candidati.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));
        if (_eseguibile is not null && Path.Combine(Path.GetDirectoryName(_eseguibile)!, "tessdata") is var dati && Directory.Exists(dati))
            _cartellaLingue = dati;
    }

    public bool Disponibile => _eseguibile is not null;

    public async Task<string> RiconosciAsync(byte[] immaginePng, CancellationToken ct)
    {
        if (_eseguibile is null) throw new InvalidOperationException("Il programma di OCR (Tesseract) non è installato.");
        var file = Path.Combine(Path.GetTempPath(), "corico-ocr-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(file, immaginePng, ct);
        try
        {
            var avvio = new ProcessStartInfo(_eseguibile)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var a in new[] { file, "stdout", "-l", "ita", "--psm", "3" }) avvio.ArgumentList.Add(a);
            if (_cartellaLingue is not null) avvio.Environment["TESSDATA_PREFIX"] = _cartellaLingue;

            using var processo = Process.Start(avvio) ?? throw new InvalidOperationException("Avvio dell'OCR non riuscito.");
            // Priorità bassa: l'OCR di una perizia di centinaia di pagine non deve rallentare chi lavora.
            try { processo.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
            var uscita = processo.StandardOutput.ReadToEndAsync(ct);
            var errori = processo.StandardError.ReadToEndAsync(ct);
            await processo.WaitForExitAsync(ct);
            if (processo.ExitCode != 0)
            {
                _log?.LogWarning("OCR non riuscito ({Codice}): {Errore}", processo.ExitCode, await errori);
                throw new InvalidOperationException("Il riconoscimento del testo (OCR) non è riuscito.");
            }
            return (await uscita).Trim();
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
