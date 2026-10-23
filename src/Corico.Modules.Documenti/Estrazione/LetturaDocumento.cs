using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Corico.Modules.Documenti.Estrazione;

public sealed record EsitoEstrazione(
    IReadOnlyList<PaginaLetta> Pagine, bool Supportato, int PagineSenzaTesto, string? Messaggio, DatiMail? Mail = null);

/// <summary>
/// Sceglie il lettore in base all'estensione. Nei PDF le pagine senza testo (scansioni) si leggono con l'OCR,
/// pagina per pagina, così i PDF misti hanno testo ovunque.
/// </summary>
public sealed class LetturaDocumento(IMotoreOcr ocr, ILogger<LetturaDocumento>? log = null)
{
    /// <summary>Risoluzione delle pagine trasformate in immagine per l'OCR.</summary>
    public const int Dpi = 300;

    public static readonly string[] EstensioniSupportate = [".pdf", ".docx", ".xlsx", ".xlsm", ".eml", ".msg", ".txt", ".csv", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"];

    public bool OcrDisponibile => ocr.Disponibile;

    /// <param name="avanzamento">Pagine lette e totali, per mostrare l'avanzamento delle scansioni lunghe.</param>
    public async Task<EsitoEstrazione> LeggiAsync(string percorso, string estensione, Action<int, int>? avanzamento, CancellationToken ct)
    {
        switch (estensione.ToLowerInvariant())
        {
            case ".pdf":
                return await LeggiPdfAsync(percorso, avanzamento, ct);
            case ".docx":
                return Una(LettoreWord.Leggi(percorso));
            case ".xlsx" or ".xlsm":
                return new(LettoreExcel.Leggi(percorso), true, 0, null);
            case ".eml":
            {
                var (testo, dati) = LettoreMail.LeggiEml(percorso);
                return Una(testo) with { Mail = dati };
            }
            case ".msg":
            {
                var (testo, dati) = LettoreMail.LeggiMsg(percorso);
                return Una(testo) with { Mail = dati };
            }
            case ".txt" or ".csv":
                return Una(await File.ReadAllTextAsync(percorso, ct));
            case ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp":
            {
                if (!ocr.Disponibile)
                    return new([new PaginaLetta(1, "")], true, 1, "Immagine non letta: il programma di OCR non è installato.");
                using var bitmap = SKBitmap.Decode(percorso) ?? throw new InvalidOperationException("Immagine non leggibile.");
                var testo = await ocr.RiconosciAsync(Png(bitmap), ct);
                return new([new PaginaLetta(1, testo, DaOcr: true)], true, 0, null);
            }
            case ".doc":
                return new([], false, 0, "Il formato Word .doc (precedente al 2007) si archivia ma non si legge: salvalo come .docx per cercare nel contenuto.");
            default:
                return new([], false, 0, $"Formato {estensione} archiviato senza lettura del contenuto.");
        }
    }

    private static EsitoEstrazione Una(string testo) => new([new PaginaLetta(1, LettorePdf.Pulisci(testo))], true, 0, null);

    private async Task<EsitoEstrazione> LeggiPdfAsync(string percorso, Action<int, int>? avanzamento, CancellationToken ct)
    {
        var pagine = LettorePdf.Leggi(percorso).ToList();
        var daLeggere = pagine.Where(p => LettorePdf.SenzaTesto(p.Testo)).Select(p => p.Numero).ToList();
        if (daLeggere.Count == 0) return new(pagine, true, 0, null);
        if (!ocr.Disponibile)
            return new(pagine, true, daLeggere.Count,
                $"{daLeggere.Count} pagine sono scansioni e non sono state lette: il programma di OCR non è installato.");

        var pdf = await File.ReadAllBytesAsync(percorso, ct);
        var fatte = 0;
        var nonRiuscite = 0;
        avanzamento?.Invoke(0, daLeggere.Count);
        foreach (var numero in daLeggere)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var immagine = PDFtoImage.Conversion.ToImage(pdf, numero - 1, options: new PDFtoImage.RenderOptions(Dpi: Dpi, WithAnnotations: true));
                var testo = await ocr.RiconosciAsync(Png(immagine), ct);
                pagine[numero - 1] = new PaginaLetta(numero, testo, DaOcr: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                nonRiuscite++;
                log?.LogWarning(ex, "OCR della pagina {Pagina} non riuscito", numero);
            }
            avanzamento?.Invoke(++fatte, daLeggere.Count);
        }
        return new(pagine, true, nonRiuscite, nonRiuscite == 0 ? null : $"{nonRiuscite} pagine scansionate non sono state lette.");
    }

    private static byte[] Png(SKBitmap bitmap)
    {
        using var dati = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return dati.ToArray();
    }
}
