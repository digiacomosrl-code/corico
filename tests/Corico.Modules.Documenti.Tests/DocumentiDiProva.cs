using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using PageSize = UglyToad.PdfPig.Content.PageSize;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Corico.Modules.Documenti.Tests;

/// <summary>Documenti costruiti al momento: niente dati aziendali nel repository.</summary>
internal static class DocumentiDiProva
{
    /// <summary>PDF con testo: ogni elemento è una pagina, ogni riga una riga di testo.</summary>
    public static byte[] PdfConTesto(params string[][] pagine)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var righe in pagine)
        {
            var pagina = builder.AddPage(PageSize.A4);
            for (var i = 0; i < righe.Length; i++)
                pagina.AddText(righe[i], 11, new PdfPoint(40, 780 - i * 16), font);
        }
        return builder.Build();
    }

    /// <summary>PDF «scansionato»: una pagina con il solo testo disegnato come immagine, senza testo selezionabile.</summary>
    public static byte[] PdfScansione(params string[] righe)
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        pagina.AddPng(Immagine(righe), new PdfRectangle(0, 0, 595, 842));
        return builder.Build();
    }

    /// <summary>Immagine PNG bianca di una pagina A4 a 150 dpi con il testo in nero.</summary>
    public static byte[] Immagine(params string[] righe)
    {
        using var bitmap = new SKBitmap(1240, 1754);
        using var tela = new SKCanvas(bitmap);
        tela.Clear(SKColors.White);
        // La versione di SkiaSharp per Linux usata nelle prove non ha caratteri di sistema: se c'è, si usa DejaVu Sans.
        const string dejavu = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        using var carattere = File.Exists(dejavu) ? SKTypeface.FromFile(dejavu) : SKTypeface.Default;
        using var font = new SKFont(carattere, 40);
        using var pennello = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        for (var i = 0; i < righe.Length; i++)
            tela.DrawText(righe[i], 100, 200 + i * 70, SKTextAlign.Left, font, pennello);
        using var dati = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return dati.ToArray();
    }

    public static byte[] Word(string[] paragrafi, string[][] tabella)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var corpo = new Body();
            foreach (var p in paragrafi) corpo.Append(new Paragraph(new Run(new Text(p))));
            var t = new Table();
            foreach (var riga in tabella)
                t.Append(new TableRow(riga.Select(c => new TableCell(new Paragraph(new Run(new Text(c))))).ToArray<OpenXmlElement>()));
            corpo.Append(t);
            main.Document = new Document(corpo);
        }
        return ms.ToArray();
    }

    public static byte[] Mail(string mittente, string oggetto, string testo, params (string Nome, byte[] Contenuto)[] allegati)
    {
        var messaggio = new MimeKit.MimeMessage();
        messaggio.From.Add(MimeKit.MailboxAddress.Parse(mittente));
        messaggio.To.Add(MimeKit.MailboxAddress.Parse("ufficio.tecnico@example.com"));
        messaggio.Subject = oggetto;
        messaggio.Date = new DateTimeOffset(2026, 2, 12, 10, 30, 0, TimeSpan.Zero);
        var corpo = new MimeKit.BodyBuilder { TextBody = testo };
        foreach (var (nome, contenuto) in allegati) corpo.Attachments.Add(nome, contenuto);
        messaggio.Body = corpo.ToMessageBody();
        using var ms = new MemoryStream();
        messaggio.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Testo lungo e vario, per le prove sui duplicati simili.</summary>
    public static string TestoOfferta(string fornitore, int seme)
    {
        var r = new Random(seme);
        string[] articoli = ["porta REI60", "cilindro Yale", "maniglione antipanico", "chiudiporta aereo", "porta REI120", "griglia di aerazione", "telaio in acciaio", "guarnizione termoespandente"];
        string[] locali = ["Deposito Rack", "QEG", "Deposito pulito", "Locale tecnico", "Centrale termica", "Archivio"];
        var righe = Enumerable.Range(1, 40).Select(i =>
            $"riga {i} {articoli[r.Next(articoli.Length)]} posizione {locali[r.Next(locali.Length)]} quantità {r.Next(1, 20)} prezzo {r.Next(10, 900)},00 sconto 43%");
        return $"Offerta {fornitore} n. {seme}\n" + string.Join("\n", righe);
    }
}
