using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Esportazioni;
using Corico.Modules.Preventivo.Importazione;
using Corico.Modules.Preventivo.Servizi;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Corico.Modules.Preventivo.Tests;

public class LettoreComputoPdfTests
{
    // Stampa tipo PriMus con capitoli, cambio pagina e quadro riepilogativo; dati di fantasia.
    private static readonly string[] StampaConCapitoli =
    [
        "Computo metrico estimativo",
        "N° Tariffa Descrizione dei lavori P.U. Lunghez. Larghez. Alt./Peso Quantità Unitario Totale",
        "Dimensioni Prezzo",
        "Computo",
        "Opere di demolizione",
        "1 21.1.7 DEMOLIZIONE E",
        "DISMISSIONE.Dismissione di lastre di",
        "marmo, escluso il trasporto a rifiuto.",
        "Via Roma, piano terra",
        "Soglia stanza 1 2,000 1,20 0,80 1,920",
        "Soglia stanza 2 1,000 1,30 0,70 0,910",
        "Sommano m2 2,830 € 19,44 € 55,02",
        "2 NP 5 Fornitura di pietra bocciardata fino a 1,5",
        "cm di spessore.",
        "Pg.1",
        "A riportare € 55,02",
        "N° Tariffa Descrizione dei lavori P.U. Lunghez. Larghez. Alt./Peso Quantità Unitario Totale",
        "Riporto € 55,02",
        "Rinquadro 1,000 8,52 8,520",
        "Sommano m 8,520 € 138,11 € 1176,70",
        "Opere di demolizione",
        "Sommano € 1231,72",
        "IMPIANTI",
        "IMPIANTO ELETTRICO",
        "3 14.1.1.1 Derivazione per punto luce semplice.",
        "Punti luce 156,000 156,000",
        "Sommano cad 156,000 € 42,78 € 6673,68",
        "IMPIANTO ELETTRICO",
        "Sommano € 6673,68",
        "Opere provvisionali e di",
        "sicurezza",
        "4 26.1.42 OPERE PROVVISIONALI.Linea vita.",
        "Linea 1,000 20,00 20,000",
        "Sommano m 20,000 € 10,00 € 200,00",
        "Opere provvisionali e di",
        "sicurezza",
        "Sommano € 200,00",
        "Totale € 8105,40",
        "IMPORTO DEI LAVORI",
        "Totale € 8105,40",
        "Pg.2",
        "Quadro riepilogativo",
        "Parti d'opera Totale Ramo Totale",
        "Computo € 8105,40",
        "Opere di demolizione € 1231,72",
        "IMPIANTI € 6673,68",
        "IMPIANTO ELETTRICO € 6673,68",
        "Opere provvisionali e di sicurezza € 200,00",
        "Totale lavori € 8105,40",
    ];

    [Fact]
    public void Stampa_con_capitoli_voci_e_totale()
    {
        var r = LettoreComputoPdf.Analizza(StampaConCapitoli);
        var l = r.Letto;
        Assert.Equal(8105.40m, r.TotaleDocumento);
        Assert.Equal([1, 2, 3, 4], l.Voci.Select(v => v.Ordine));
        Assert.Contains(l.Segnalazioni, s => s.Testo.StartsWith("Controllo superato"));

        var prima = l.Elenco(l.Voci[0].IdElenco)!;
        Assert.Equal(("21.1.7", "mq", 19.44m), (prima.Tariffa, prima.Unita, prima.Prezzo));
        Assert.Equal(2.830m, l.Voci[0].Quantita);
        Assert.StartsWith("DEMOLIZIONE E DISMISSIONE.Dismissione di lastre di marmo", prima.DesEstesa);
        Assert.Equal(2, l.Voci[0].Misure.Count(m => m.Quantita is not null));

        // «NP 5» è una tariffa in due parti; «fino a 1,5» resta nella descrizione; il cambio pagina non spezza la voce.
        var seconda = l.Elenco(l.Voci[1].IdElenco)!;
        Assert.Equal("NP 5", seconda.Tariffa);
        Assert.Equal("Fornitura di pietra bocciardata fino a 1,5 cm di spessore.", seconda.DesEstesa);
        Assert.Equal(8.520m, l.Voci[1].Quantita);

        // Capitoli e livelli dal quadro riepilogativo; il titolo su due righe si ricompone.
        Assert.Equal(
            [("SuperCategoria", "Opere di demolizione"), ("SuperCategoria", "IMPIANTI"), ("Categoria", "IMPIANTO ELETTRICO"),
             ("SuperCategoria", "Opere provvisionali e di sicurezza")],
            l.Categorie.Select(c => (c.Livello, c.Descrizione)));
        var cat = l.Categorie.ToDictionary(c => c.Descrizione, c => c.Id);
        Assert.Equal((cat["IMPIANTI"], cat["IMPIANTO ELETTRICO"]), (l.Voci[2].IdSuperCategoria, l.Voci[2].IdCategoria));
        Assert.Equal(cat["Opere provvisionali e di sicurezza"], l.Voci[3].IdSuperCategoria);
    }

    [Fact]
    public void Stampa_con_sommano_uguale_e_numero_di_elenco()
    {
        string[] righe =
        [
            "Ente di prova",                       // frontespizio: non è un capitolo
            "Pag. 1",
            "N. N.E. D E S C R I Z I O N E Parti U Lunghezza Larghezza Alt/Peso Quantita' Prezzo Un. Importo",
            "1 NP. SGO.LOC",
            "1 Sgombero e conferimento a impianto autorizzato.",
            "SAL 1",
            "Sgombero locali",
            "1 1,00 1,000",
            "SOMMANO ac = 1,000 15.933,00 15.933,00",
            "2 035283",
            "Segnalatore ottico in contenitore isolante.",
            "SAL 1",
            "2 2,00 2,00",
            "SOMMANO cad = 2,00 31,97 63,94",
            "A RIPORTARE 15.996,94",
            "RIEPILOGO CAPITOLI Pag. Importo Paragr. Importo subCap. IMPORTO",
            "1 15.996,94",
            "Sommano i lavori € 15.996,94",
        ];
        var r = LettoreComputoPdf.Analizza(righe);
        Assert.Empty(r.Letto.Categorie);
        Assert.Equal(15996.94m, r.TotaleDocumento);
        var e1 = r.Letto.Elenco(r.Letto.Voci[0].IdElenco)!;
        Assert.Equal(("NP. SGO.LOC", "Sgombero e conferimento a impianto autorizzato.", 15933m), (e1.Tariffa, e1.DesEstesa, e1.Prezzo));
        var e2 = r.Letto.Elenco(r.Letto.Voci[1].IdElenco)!;
        Assert.Equal(("035283", "Segnalatore ottico in contenitore isolante."), (e2.Tariffa, e2.DesEstesa));
        Assert.Contains(r.Letto.Segnalazioni, s => s.Testo.StartsWith("Controllo superato"));
    }

    [Fact]
    public void Voce_non_riconosciuta_segnalata_dal_controllo_del_totale()
    {
        var righe = StampaConCapitoli.Where(r => !r.StartsWith("3 14.1.1.1")).ToArray();
        var r = LettoreComputoPdf.Analizza(righe);
        Assert.Equal(3, r.Letto.Voci.Count);
        Assert.Contains(r.Letto.Segnalazioni, s => s.Gravita == GravitaSegnalazione.Attenzione && s.Testo.Contains("diversa dal totale"));
    }

    [Fact]
    public void Pdf_vero_anche_firmato_p7m()
    {
        var pdf = CreaPdf(StampaConCapitoli);
        var anteprima = ImportazioneComputoService.Anteprima(new MemoryStream(pdf), "computo.pdf");
        Assert.Equal((OrigineComputo.Pdf, 4, 8105.40m), (anteprima.Origine, anteprima.Letto.Voci.Count, anteprima.TotaleCalcolato));
        Assert.Equal(0m, anteprima.Differenza);

        var firmato = FirmaP7m(pdf);
        var daP7m = ImportazioneComputoService.Anteprima(new MemoryStream(firmato), "computo.pdf.p7m");
        Assert.Equal(8105.40m, daP7m.TotaleCalcolato);
        var base64 = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(firmato, Base64FormattingOptions.InsertLineBreaks));
        Assert.Equal(8105.40m, ImportazioneComputoService.Anteprima(new MemoryStream(base64), "computo.p7m").TotaleCalcolato);
    }

    [Fact]
    public void File_non_adatti_spiegati()
    {
        var nonPdf = Assert.Throws<FormatException>(() => ImportazioneComputoService.Anteprima(new MemoryStream("ciao"u8.ToArray()), "x.pdf"));
        Assert.Contains("non è un PDF", nonPdf.Message);
        var vuoto = Assert.Throws<FormatException>(() => ImportazioneComputoService.Anteprima(new MemoryStream(CreaPdf([])), "scansione.pdf"));
        Assert.Contains("scansione", vuoto.Message);
    }

    private static byte[] CreaPdf(IReadOnlyList<string> righe)
    {
        CaratteriStampa.Registra();
        using var doc = new PdfDocument();
        var font = new XFont(CaratteriStampa.Famiglia, 9);
        for (var inizio = 0; inizio < Math.Max(righe.Count, 1); inizio += 60)
        {
            var pagina = doc.AddPage();
            using var g = XGraphics.FromPdfPage(pagina);
            foreach (var (riga, i) in righe.Skip(inizio).Take(60).Select((r, i) => (r, i)))
                g.DrawString(riga, font, XBrushes.Black, 40, 40 + i * 12);
        }
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] FirmaP7m(byte[] contenuto)
    {
        using var chiave = RSA.Create(2048);
        var richiesta = new CertificateRequest("CN=Firma di prova", chiave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificato = richiesta.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        var cms = new SignedCms(new ContentInfo(contenuto), detached: false);
        cms.ComputeSignature(new CmsSigner(certificato));
        return cms.Encode();
    }
}
