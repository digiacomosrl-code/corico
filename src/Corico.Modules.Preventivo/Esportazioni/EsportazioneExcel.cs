using ClosedXML.Excel;
using Corico.Modules.Preventivo.Interfaccia;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Esportazioni;

/// <summary>Esportazioni Excel del preventivo, con lo stesso stile sobrio delle stampe PDF.</summary>
public static class EsportazioneExcel
{
    private const string FormatoEuro = "#,##0.00 €;-#,##0.00 €";
    private const string FormatoNumero = "#,##0.00##";
    private const string FormatoPercento = "0.00\" %\"";

    public static byte[] Computo(IntestazioneStampa intestazione, Fotografia f)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Computo");
        var riga = Intestazione(ws, intestazione, "Computo metrico con ricavi netti", 11);

        var pacchetti = f.Pacchetti.ToDictionary(p => p.Id, p => p.Descrizione);
        string[] titoli = ["N.", "Tariffa", "Descrizione", "U.M.", "Quantità", "Prezzo", "Importo lordo",
            "% non soggetta", "Ricavo netto", "Pacchetto", "Q. pianificazione"];
        var primaRigaDati = Titoli(ws, riga, titoli);
        riga = primaRigaDati;
        var n = 0;
        foreach (var v in f.Voci)
        {
            ws.Cell(riga, 1).Value = ++n;
            ws.Cell(riga, 2).Value = v.Tariffa;
            ws.Cell(riga, 3).Value = v.Descrizione;
            ws.Cell(riga, 4).Value = v.Unita;
            ws.Cell(riga, 5).Value = v.Quantita;
            ws.Cell(riga, 6).Value = v.Prezzo;
            ws.Cell(riga, 7).FormulaA1 = $"ROUND(E{riga}*F{riga},2)";
            ws.Cell(riga, 8).Value = v.PercentualeNonSoggetta;
            ws.Cell(riga, 9).Value = v.RicavoNetto;
            ws.Cell(riga, 10).Value = v.PacchettoId is { } id ? pacchetti.GetValueOrDefault(id) : "";
            if (v.QuantitaPianificazione is { } qp) ws.Cell(riga, 11).Value = qp;
            riga++;
        }
        ws.Cell(riga, 3).Value = "Totale";
        ws.Cell(riga, 7).FormulaA1 = $"SUM(G{primaRigaDati}:G{riga - 1})";
        ws.Cell(riga, 9).FormulaA1 = $"SUM(I{primaRigaDati}:I{riga - 1})";
        StileTotale(ws.Range(riga, 1, riga, titoli.Length));
        StileCorpo(ws.Range(primaRigaDati, 1, riga - 1, titoli.Length));

        ws.Range(primaRigaDati, 5, riga, 5).Style.NumberFormat.Format = FormatoNumero;
        ws.Range(primaRigaDati, 11, riga, 11).Style.NumberFormat.Format = FormatoNumero;
        foreach (var c in new[] { 6, 7, 9 }) ws.Range(primaRigaDati, c, riga, c).Style.NumberFormat.Format = FormatoEuro;
        ws.Range(primaRigaDati, 8, riga, 8).Style.NumberFormat.Format = FormatoPercento;
        Larghezze(ws, 5, 14, 60, 7, 12, 12, 15, 10, 15, 30, 12);
        ws.Column(3).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(primaRigaDati - 1);
        Pagina(ws, orizzontale: true, primaRigaDati - 1);
        return Salva(wb);
    }

    public static byte[] Riepilogo(IntestazioneStampa intestazione, Fotografia f)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Riepilogo");
        var riga = Intestazione(ws, intestazione, "Riepilogo economico", 3);
        var t = f.Totali;

        riga = Sezione(ws, riga, "Ricavi", [
            ("Importo lavori (lordo)", t.ImportoLavoriLordo, false),
            ("di cui non soggetto a ribasso", t.ImportoNonSoggetto, false),
            ($"Ribasso d'asta ({Corico.Core.Calcolo.Formato.Percento(f.RibassoPercentuale)})", -t.ImportoRibasso, false),
            ("Ricavo netto da computo", t.RicavoNettoComputo, true),
            ("Ricavo dei pacchetti (quantità di pianificazione e integrazioni)", t.RicavoPacchetti, false),
            ("Ricavo delle voci non assegnate", t.RicavoNonAssegnato, false),
            ("Integrazioni di ricavo di commessa", t.IntegrazioniCommessa, false),
            ("Ricavo di commessa", t.Ricavo, true),
        ]);
        riga = Sezione(ws, riga + 1, "Costi e risultato", [
            ("Costo pianificato (offerte scelte)", t.Costo, false),
            ("MDC", t.Mdc, true),
            ("Spese generali", -t.SpeseGenerali, false),
            ("Risultato", t.Risultato, true),
        ]);
        riga = SezionePercentuali(ws, riga + 1, [
            ("Margine sui ricavi (MDC / ricavo)", t.MarginePercentuale),
            ("Ricarico sui costi (MDC / costo)", t.RicaricoPercentuale),
            ("Risultato sui ricavi", t.RisultatoPercentuale),
            ("Obiettivo di margine", f.ObiettivoMarginePercentuale),
            ("Scostamento dall'obiettivo", t.MarginePercentuale is { } m && f.ObiettivoMarginePercentuale is { } o ? m - o : null),
        ]);
        if (t.RicavoSenzaCosto != 0)
        {
            ws.Cell(riga + 1, 1).Value = $"Attenzione: {Corico.Core.Calcolo.Formato.Euro(t.RicavoSenzaCosto)} di ricavo non hanno ancora un costo pianificato.";
            ws.Cell(riga + 1, 1).Style.Font.Italic = true;
        }
        Larghezze(ws, 62, 18, 4);
        Pagina(ws, orizzontale: false, 0);

        if (f.SpeseGenerali.Count > 0)
        {
            var sg = wb.AddWorksheet("Spese generali");
            var r = Titoli(sg, 1, ["Descrizione", "Modo", "Percentuale", "Base", "Importo", "Note"]);
            var inizio = r;
            foreach (var s in f.SpeseGenerali)
            {
                sg.Cell(r, 1).Value = s.Descrizione;
                sg.Cell(r, 2).Value = s.Modo == Dominio.ModoSpesaGenerale.Importo ? "Importo" : "Percentuale";
                if (s.Modo == Dominio.ModoSpesaGenerale.Percentuale)
                {
                    sg.Cell(r, 3).Value = s.Percentuale;
                    sg.Cell(r, 4).Value = Testi.BaseSpesa(s.Base);
                }
                sg.Cell(r, 5).Value = s.ImportoCalcolato;
                sg.Cell(r, 6).Value = s.Note;
                r++;
            }
            sg.Cell(r, 1).Value = "Totale";
            sg.Cell(r, 5).FormulaA1 = $"SUM(E{inizio}:E{r - 1})";
            StileCorpo(sg.Range(inizio, 1, r - 1, 6));
            StileTotale(sg.Range(r, 1, r, 6));
            sg.Range(inizio, 3, r, 3).Style.NumberFormat.Format = FormatoPercento;
            sg.Range(inizio, 5, r, 5).Style.NumberFormat.Format = FormatoEuro;
            Larghezze(sg, 40, 12, 12, 22, 15, 40);
            Pagina(sg, orizzontale: false, 1);
        }

        var wp = wb.AddWorksheet("Pacchetti");
        string[] titoli = ["N.", "Pacchetto", "Affidamento", "Fornitore", "Stato", "Ricavo", "Costo", "MDC", "MDC %"];
        var rp = Titoli(wp, 1, titoli);
        var primo = rp;
        foreach (var p in f.Pacchetti)
        {
            wp.Cell(rp, 1).Value = p.Ordine;
            wp.Cell(rp, 2).Value = p.Descrizione;
            wp.Cell(rp, 3).Value = Testi.Affidamento(p.Tipo);
            wp.Cell(rp, 4).Value = p.Fornitore ?? (p.NumeroOfferte == 0 ? "Nessuna offerta" : "Offerta da scegliere");
            wp.Cell(rp, 5).Value = Testi.Stato(p.Stato);
            wp.Cell(rp, 6).Value = p.Ricavo;
            if (p.Costo is { } c)
            {
                wp.Cell(rp, 7).Value = c;
                wp.Cell(rp, 8).FormulaA1 = $"F{rp}-G{rp}";
                wp.Cell(rp, 9).FormulaA1 = $"IF(F{rp}=0,\"n.d.\",H{rp}/F{rp}*100)";
            }
            rp++;
        }
        wp.Cell(rp, 2).Value = "Totale pacchetti";
        wp.Cell(rp, 6).FormulaA1 = $"SUM(F{primo}:F{rp - 1})";
        wp.Cell(rp, 7).FormulaA1 = $"SUM(G{primo}:G{rp - 1})";
        StileCorpo(wp.Range(primo, 1, rp - 1, titoli.Length));
        StileTotale(wp.Range(rp, 1, rp, titoli.Length));
        foreach (var c in new[] { 6, 7, 8 }) wp.Range(primo, c, rp, c).Style.NumberFormat.Format = FormatoEuro;
        wp.Range(primo, 9, rp, 9).Style.NumberFormat.Format = FormatoPercento;
        Larghezze(wp, 5, 45, 18, 30, 12, 15, 15, 15, 9);
        wp.SheetView.FreezeRows(1);
        Pagina(wp, orizzontale: true, 1);

        return Salva(wb);
    }

    // ---- Stile comune ----

    private static int Intestazione(IXLWorksheet ws, IntestazioneStampa i, string titolo, int colonne)
    {
        ws.Cell(1, 1).Value = titolo;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.FromHtml(Stile.Primario));
        ws.Cell(2, 1).Value = i.Commessa;
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = i.Sottotitolo;
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml(Stile.TestoSecondario);
        ws.Range(1, 1, 1, colonne).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range(1, 1, 1, colonne).Style.Border.BottomBorderColor = XLColor.FromHtml(Stile.Primario);
        return 5;
    }

    private static int Titoli(IXLWorksheet ws, int riga, string[] titoli)
    {
        for (var c = 0; c < titoli.Length; c++) ws.Cell(riga, c + 1).Value = titoli[c];
        var r = ws.Range(riga, 1, riga, titoli.Length);
        r.Style.Font.Bold = true;
        r.Style.Font.FontColor = XLColor.White;
        r.Style.Fill.BackgroundColor = XLColor.FromHtml(Stile.Primario);
        r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        r.Style.Alignment.WrapText = true;
        return riga + 1;
    }

    private static void StileCorpo(IXLRange r)
    {
        if (r.RowCount() == 0 || r.FirstRow().RowNumber() > r.LastRow().RowNumber()) return;
        r.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        r.Style.Border.BottomBorderColor = XLColor.FromHtml(Stile.Bordo);
        r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        foreach (var riga in r.Rows().Where((_, i) => i % 2 == 1))
            riga.Style.Fill.BackgroundColor = XLColor.FromHtml(Stile.RigaAlterna);
    }

    private static void StileTotale(IXLRange r)
    {
        r.Style.Font.Bold = true;
        r.Style.Fill.BackgroundColor = XLColor.FromHtml(Stile.Totale);
        r.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        r.Style.Border.TopBorderColor = XLColor.FromHtml(Stile.Primario);
    }

    private static int Sezione(IXLWorksheet ws, int riga, string titolo, (string Voce, decimal Importo, bool Totale)[] righe)
    {
        riga = Titoli(ws, riga, [titolo, "Importo"]);
        foreach (var (voce, importo, totale) in righe)
        {
            ws.Cell(riga, 1).Value = voce;
            ws.Cell(riga, 2).Value = importo;
            ws.Cell(riga, 2).Style.NumberFormat.Format = FormatoEuro;
            if (totale) StileTotale(ws.Range(riga, 1, riga, 2));
            else ws.Range(riga, 1, riga, 2).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            riga++;
        }
        return riga;
    }

    private static int SezionePercentuali(IXLWorksheet ws, int riga, (string Voce, decimal? Valore)[] righe)
    {
        riga = Titoli(ws, riga, ["Indicatori", "%"]);
        foreach (var (voce, valore) in righe)
        {
            ws.Cell(riga, 1).Value = voce;
            if (valore is { } v) ws.Cell(riga, 2).Value = v;
            else ws.Cell(riga, 2).Value = "n.d.";
            ws.Cell(riga, 2).Style.NumberFormat.Format = FormatoPercento;
            ws.Cell(riga, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(riga, 1, riga, 2).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            riga++;
        }
        return riga;
    }

    private static void Larghezze(IXLWorksheet ws, params double[] larghezze)
    {
        for (var i = 0; i < larghezze.Length; i++) ws.Column(i + 1).Width = larghezze[i];
    }

    private static void Pagina(IXLWorksheet ws, bool orizzontale, int rigaTitoli)
    {
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = orizzontale ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.SetLeft(0.5).SetRight(0.5).SetTop(0.6).SetBottom(0.6);
        if (rigaTitoli > 0) ws.PageSetup.SetRowsToRepeatAtTop(rigaTitoli, rigaTitoli);
        ws.PageSetup.Footer.Center.AddText("Pagina &P di &N");
        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 9;
    }

    private static byte[] Salva(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
