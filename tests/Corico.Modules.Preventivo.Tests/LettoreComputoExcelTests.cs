using ClosedXML.Excel;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class LettoreComputoExcelTests
{
    private static byte[] Cartella(Action<IXLWorksheet> scrivi)
    {
        using var wb = new XLWorkbook();
        scrivi(wb.AddWorksheet("Computo"));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Formato_tabellare_con_capitoli_e_articoli_ripetuti()
    {
        var file = Cartella(ws =>
        {
            string[] intestazione = ["V", "N°", "Corpo", "Codice", "Descrizione", "D", "Un.Mis.", "Prezzo Appl.", "Quantità", "Quantità Tot.", "Importo"];
            for (var c = 0; c < intestazione.Length; c++) ws.Cell(2, c + 1).Value = intestazione[c];
            ws.Cell(3, 4).Value = "»Cap.1"; ws.Cell(3, 5).Value = "Computo"; ws.Cell(3, 11).Value = 1_315.78;
            ws.Cell(4, 4).Value = "»Cap.1 Sc.1"; ws.Cell(4, 5).Value = "Demolizioni";
            ws.Cell(5, 2).Value = "1"; ws.Cell(5, 4).Value = "21.1.7"; ws.Cell(5, 5).Value = "Dismissione di lastre"; ws.Cell(5, 7).Value = "m²";
            ws.Cell(5, 8).Value = "19.44"; ws.Cell(5, 9).Value = "11.1"; ws.Cell(5, 10).Value = "61.1"; ws.Cell(5, 11).Value = "215.78";
            ws.Cell(6, 5).Value = "es";
            ws.Cell(7, 2).Value = "2"; ws.Cell(7, 4).Value = "21.1.7"; ws.Cell(7, 5).Value = "Dismissione di lastre"; ws.Cell(7, 7).Value = "m²";
            ws.Cell(7, 8).Value = "19.44"; ws.Cell(7, 9).Value = "50"; ws.Cell(7, 10).Value = "61.1"; ws.Cell(7, 11).Value = "972";
            ws.Cell(8, 2).Value = "3"; ws.Cell(8, 4).Value = "NP1"; ws.Cell(8, 5).Value = "Opera a corpo"; ws.Cell(8, 7).Value = "a corpo";
            ws.Cell(8, 8).Value = "128"; ws.Cell(8, 9).Value = "1"; ws.Cell(8, 10).Value = "1"; ws.Cell(8, 11).Value = "128";
        });

        var r = LettoreComputoExcel.Leggi(new MemoryStream(file));
        Assert.Equal("Excel tabellare", r.Formato);
        Assert.Equal(3, r.Letto.Voci.Count);
        Assert.Equal(2, r.Letto.ElencoPrezzi.Count);             // lo stesso articolo ripetuto ha un solo prezzo di elenco
        Assert.Equal(1_315.78m, r.TotaleDocumento);
        Assert.Equal("mq", r.Letto.ElencoPrezzi[0].Unita);
        Assert.Equal("Cap.1 Sc.1", r.Letto.Voci[0].IdCategoria);
        Assert.Contains(r.Letto.Segnalazioni, s => s.Testo.Contains("Controllo superato"));

        var a = ImportazioneComputoService.Anteprima(new MemoryStream(file), "computo.xlsx");
        Assert.Equal(OrigineComputo.Excel, a.Origine);
        Assert.Equal(1_315.78m, a.TotaleCalcolato);
        Assert.Equal(0m, a.Differenza);
    }

    [Fact]
    public void Stampa_primus_con_misure_e_sommano()
    {
        var file = Cartella(ws =>
        {
            ws.Cell(2, 2).Value = "Nr. Ord."; ws.Cell(2, 3).Value = "TARIFFA"; ws.Cell(2, 4).Value = "DESIGNAZIONE DEI LAVORI";
            ws.Cell(2, 6).Value = "M I S U R A Z I O N I:"; ws.Cell(2, 9).Value = "Quantità"; ws.Cell(2, 10).Value = "         IMPORTI";
            ws.Cell(3, 5).Value = "Par.ug"; ws.Cell(3, 6).Value = "Lung."; ws.Cell(3, 7).Value = "Larg."; ws.Cell(3, 8).Value = "H/peso";
            ws.Cell(3, 10).Value = "unitario"; ws.Cell(3, 11).Value = "TOTALE";
            ws.Cell(4, 2).Value = "1"; ws.Cell(4, 3).Value = "21.1.5.1"; ws.Cell(4, 4).Value = "Demolizione di muratura di qualsiasi tipo";
            ws.Cell(5, 4).Value = "M I S U R A Z I O N I:";
            ws.Cell(6, 4).Value = "PIANO TERRA"; ws.Cell(6, 8).Value = "0"; ws.Cell(6, 9).Value = "0.0";
            ws.Cell(7, 4).Value = "1,81x1,60x1,26"; ws.Cell(7, 5).Value = "3.65"; ws.Cell(7, 9).Value = "3.65";
            ws.Cell(8, 4).Value = "a detrarre"; ws.Cell(8, 5).Value = "-1.53"; ws.Cell(8, 9).Value = "-1.53";
            ws.Cell(9, 4).Value = "Parziale * Parziale m3"; ws.Cell(9, 5).Value = "2.12"; ws.Cell(9, 9).Value = "2.12";
            ws.Cell(10, 4).Value = "SOMMANO m3"; ws.Cell(10, 9).Value = "2.12"; ws.Cell(10, 10).Value = "291.02"; ws.Cell(10, 11).Value = "616.96";
            ws.Cell(12, 2).Value = "2"; ws.Cell(12, 3).Value = "NP 1"; ws.Cell(12, 4).Value = "Pulitura dell'area di cantiere";
            ws.Cell(13, 4).Value = "ore 120"; ws.Cell(13, 5).Value = "120"; ws.Cell(13, 9).Value = "120";
            ws.Cell(14, 4).Value = "SOMMANO h"; ws.Cell(14, 9).Value = "120"; ws.Cell(14, 10).Value = "34.67"; ws.Cell(14, 11).Value = "4160.4";
            ws.Cell(16, 4).Value = "TOTALE euro"; ws.Cell(16, 11).Value = "4777.36";
        });

        var r = LettoreComputoExcel.Leggi(new MemoryStream(file));
        Assert.Equal("Stampa PriMus per Excel", r.Formato);
        Assert.Equal(2, r.Letto.Voci.Count);
        Assert.Equal(2.12m, r.Letto.Voci[0].Quantita);
        Assert.Equal(3, r.Letto.Voci[0].Misure.Count);            // titolo, misura positiva, detrazione (non il parziale)
        Assert.Null(r.Letto.Voci[0].Misure[0].Quantita);
        Assert.Equal("mc", r.Letto.ElencoPrezzi[0].Unita);
        Assert.Equal(4_777.36m, r.TotaleDocumento);
        Assert.Equal(4_777.36m, ImportazioneComputoService.Anteprima(new MemoryStream(file), "cm.xlsx").TotaleCalcolato);
    }

    [Fact]
    public void Campione_computo_albergo_excel()
    {
        if (LettoreFogliCrTests.Campione("COMPUTO_ALBERGO.XLSX") is not { } percorso) return;
        using var f = File.OpenRead(percorso);
        var a = ImportazioneComputoService.Anteprima(f, percorso);
        Assert.Equal(1_998_980.93m, a.TotaleCalcolato);           // stesso totale del file XPWE
        Assert.Equal(1_998_980.93m, a.TotaleDocumento);
        Assert.DoesNotContain(a.Letto.Segnalazioni, s => s.Testo.Contains("non coincide"));
    }

    [Fact]
    public void Campione_stampa_primus_excel()
    {
        if (LettoreFogliCrTests.Campione("CM xls.xlsx") is not { } percorso) return;
        using var f = File.OpenRead(percorso);
        var a = ImportazioneComputoService.Anteprima(f, percorso);
        Assert.Equal(314_556.16m, a.TotaleDocumento);
        Assert.Equal(314_556.16m, a.TotaleCalcolato);
        Assert.Equal(28, a.Letto.Voci.Count);
    }

    [Fact]
    public async Task Importazione_del_computo_excel_nella_commessa()
    {
        if (LettoreFogliCrTests.Campione("CM xls.xlsx") is not { } percorso) return;
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Prova computo Excel")).Id;
        await amb.Get<ImportazioneComputoService>().ConfermaAsync(id, await File.ReadAllBytesAsync(percorso), "CM xls.xlsx", null);
        var stato = await amb.Get<PreventivoService>().StatoAsync(id);
        Assert.Equal(OrigineComputo.Excel, stato.Computo!.Origine);
        Assert.Equal(314_556.16m, stato.Computo.TotaleDichiarato);
        Assert.Equal(314_556.16m, stato.Quadro.ImportoLavoriLordo);
    }
}
