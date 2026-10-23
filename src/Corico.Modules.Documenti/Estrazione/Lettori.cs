using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Wp = DocumentFormat.OpenXml.Wordprocessing;

namespace Corico.Modules.Documenti.Estrazione;

/// <summary>Testo di una pagina letta; <c>DaOcr</c> se ricavato dall'immagine.</summary>
public sealed record PaginaLetta(int Numero, string Testo, bool DaOcr = false);

/// <summary>Allegato trovato in una mail, da salvare nell'archivio come documento a sé.</summary>
public sealed record AllegatoLetto(string Nome, string? TipoContenuto, byte[] Contenuto);

public sealed record DatiMail(string? Mittente, string? Oggetto, DateTime? Data, IReadOnlyList<AllegatoLetto> Allegati);

public static class LettorePdf
{
    /// <summary>Sotto questa soglia di caratteri (spazi esclusi) la pagina si considera una scansione da leggere con l'OCR.</summary>
    public const int MinimoCaratteri = 20;

    /// <summary>Testo di ogni pagina nell'ordine di lettura; le pagine scansionate restano vuote.</summary>
    public static IReadOnlyList<PaginaLetta> Leggi(string percorso)
    {
        using var pdf = PdfDocument.Open(percorso);
        return pdf.GetPages().Select(p => new PaginaLetta(p.Number, Pulisci(ContentOrderTextExtractor.GetText(p)))).ToList();
    }

    public static bool SenzaTesto(string testo) => testo.Count(c => !char.IsWhiteSpace(c)) < MinimoCaratteri;

    internal static string Pulisci(string testo) => testo.Replace("\0", "").Trim();
}

public static class LettoreWord
{
    /// <summary>Paragrafi e tabelle del documento (.docx), una riga per paragrafo e celle separate da tabulazione.</summary>
    public static string Leggi(string percorso)
    {
        using var doc = WordprocessingDocument.Open(percorso, false);
        var corpo = doc.MainDocumentPart?.Document?.Body;
        if (corpo is null) return "";
        var sb = new StringBuilder();
        foreach (var elemento in corpo.ChildElements)
        {
            if (elemento is Wp.Table tabella)
                foreach (var riga in tabella.Elements<Wp.TableRow>())
                    sb.AppendLine(string.Join('\t', riga.Elements<Wp.TableCell>().Select(c => c.InnerText.Trim())));
            else if (elemento is Wp.Paragraph p)
                sb.AppendLine(p.InnerText);
        }
        return sb.ToString().Trim();
    }
}

public static class LettoreExcel
{
    /// <summary>Un foglio per pagina, una riga per riga del foglio con le celle separate da tabulazione.</summary>
    public static IReadOnlyList<PaginaLetta> Leggi(string percorso)
    {
        using var doc = SpreadsheetDocument.Open(percorso, false);
        var cartella = doc.WorkbookPart;
        if (cartella?.Workbook?.Sheets is null) return [];
        var stringhe = cartella.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
        var pagine = new List<PaginaLetta>();
        foreach (var foglio in cartella.Workbook.Sheets.Elements<Sheet>())
        {
            if (foglio.Id?.Value is not { } id || cartella.GetPartById(id) is not WorksheetPart parte) continue;
            var sb = new StringBuilder($"{foglio.Name}\n");
            foreach (var riga in parte.Worksheet?.Descendants<Row>() ?? [])
            {
                var celle = riga.Elements<Cell>().Select(c => Valore(c, stringhe)).Where(v => v.Length > 0).ToList();
                if (celle.Count > 0) sb.AppendLine(string.Join('\t', celle));
            }
            pagine.Add(new PaginaLetta(pagine.Count + 1, sb.ToString().Trim()));
        }
        return pagine;
    }

    private static string Valore(Cell c, List<string> stringhe)
    {
        var v = c.CellValue?.Text ?? c.InnerText;
        if (c.DataType?.Value == CellValues.SharedString && int.TryParse(v, out var i) && i < stringhe.Count) return stringhe[i].Trim();
        return v.Trim();
    }
}

public static class LettoreMail
{
    /// <summary>File .eml (MIME): testo della mail e allegati.</summary>
    public static (string Testo, DatiMail Dati) LeggiEml(string percorso)
    {
        var messaggio = MimeKit.MimeMessage.Load(percorso);
        var testo = messaggio.TextBody ?? DaHtml(messaggio.HtmlBody);
        var allegati = new List<AllegatoLetto>();
        foreach (var parte in messaggio.Attachments.OfType<MimeKit.MimePart>())
        {
            using var ms = new MemoryStream();
            parte.Content?.DecodeTo(ms);
            allegati.Add(new(parte.FileName ?? "allegato", parte.ContentType?.MimeType, ms.ToArray()));
        }
        var dati = new DatiMail(messaggio.From?.ToString(), messaggio.Subject,
            messaggio.Date == DateTimeOffset.MinValue ? null : messaggio.Date.UtcDateTime, allegati);
        return (Intestazione(dati) + testo, dati);
    }

    /// <summary>File .msg di Outlook: testo della mail e allegati.</summary>
    public static (string Testo, DatiMail Dati) LeggiMsg(string percorso)
    {
        using var messaggio = new MsgReader.Outlook.Storage.Message(percorso);
        var testo = messaggio.BodyText ?? DaHtml(messaggio.BodyHtml);
        var allegati = messaggio.Attachments.OfType<MsgReader.Outlook.Storage.Attachment>()
            .Where(a => a.Data is { Length: > 0 })
            .Select(a => new AllegatoLetto(a.FileName ?? "allegato", a.MimeType, a.Data))
            .ToList();
        var mittente = messaggio.Sender is { } s ? $"{s.DisplayName} <{s.Email}>".Trim() : null;
        var dati = new DatiMail(mittente, messaggio.Subject, messaggio.SentOn?.UtcDateTime, allegati);
        return (Intestazione(dati) + testo, dati);
    }

    private static string Intestazione(DatiMail d) =>
        $"Da: {d.Mittente}\nOggetto: {d.Oggetto}\n{(d.Data is { } x ? $"Data: {x.ToLocalTime():dd/MM/yyyy HH:mm}\n" : "")}" +
        (d.Allegati.Count > 0 ? $"Allegati: {string.Join(", ", d.Allegati.Select(a => a.Nome))}\n" : "") + "\n";

    /// <summary>Testo leggibile da una mail solo HTML: niente tag, a capo ai paragrafi.</summary>
    internal static string DaHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var t = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<(br|/p|/div|/tr|/li)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(t).Trim();
    }
}
