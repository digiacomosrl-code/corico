using Corico.Core.Calcolo;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Interfaccia;
using Corico.Modules.Preventivo.Servizi;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Corico.Modules.Preventivo.Esportazioni;

/// <summary>Stampe PDF in formato A4: riepilogo economico (verticale) e scheda pacchetto (orizzontale).</summary>
public static class EsportazionePdf
{
    private const string Carattere = CaratteriStampa.Famiglia;

    static EsportazionePdf() => CaratteriStampa.Registra();

    public static byte[] Riepilogo(IntestazioneStampa intestazione, Fotografia f, IReadOnlyList<Avviso>? avvisi)
    {
        var (doc, sec) = Documento(intestazione, "Riepilogo economico", orizzontale: false);
        var t = f.Totali;

        Titoletto(sec, "Ricavi");
        var tab = Tabella(sec, ("Voce", 13.5, false), ("Importo", 4.5, true));
        Riga(tab, "Importo lavori (lordo)", Formato.Euro(t.ImportoLavoriLordo));
        Riga(tab, "di cui non soggetto a ribasso", Formato.Euro(t.ImportoNonSoggetto));
        Riga(tab, $"Ribasso d'asta ({Formato.Percento(f.RibassoPercentuale)})", Formato.Euro(-t.ImportoRibasso));
        RigaTotale(tab, "Ricavo netto da computo", Formato.Euro(t.RicavoNettoComputo));
        Riga(tab, "Ricavo dei pacchetti (quantità di pianificazione e integrazioni)", Formato.Euro(t.RicavoPacchetti));
        Riga(tab, $"Ricavo delle voci non assegnate ({t.VociNonAssegnate})", Formato.Euro(t.RicavoNonAssegnato));
        Riga(tab, "Integrazioni di ricavo di commessa", Formato.Euro(t.IntegrazioniCommessa));
        RigaTotale(tab, "Ricavo di commessa", Formato.Euro(t.Ricavo));

        Titoletto(sec, "Costi e risultato");
        tab = Tabella(sec, ("Voce", 13.5, false), ("Importo", 4.5, true));
        Riga(tab, "Costo pianificato (offerte scelte)", Formato.Euro(t.Costo));
        RigaTotale(tab, "MDC", Formato.Euro(t.Mdc));
        Riga(tab, "Spese generali", Formato.Euro(-t.SpeseGenerali));
        RigaTotale(tab, "Risultato", Formato.Euro(t.Risultato));

        Titoletto(sec, "Indicatori");
        tab = Tabella(sec, ("Indicatore", 13.5, false), ("Valore", 4.5, true));
        Riga(tab, "Margine sui ricavi (MDC / ricavo)", Formato.Percento(t.MarginePercentuale));
        Riga(tab, "Ricarico sui costi (MDC / costo)", Formato.Percento(t.RicaricoPercentuale));
        Riga(tab, "Risultato sui ricavi", Formato.Percento(t.RisultatoPercentuale));
        if (f.ObiettivoMarginePercentuale is { } obiettivo)
        {
            Riga(tab, "Obiettivo di margine", Formato.Percento(obiettivo));
            Riga(tab, "Scostamento dall'obiettivo", Formato.Percento(t.MarginePercentuale - obiettivo));
        }
        if (t.RicavoSenzaCosto != 0)
            Nota(sec, $"Attenzione: {Formato.Euro(t.RicavoSenzaCosto)} di ricavo non hanno ancora un costo pianificato " +
                      $"({t.PacchettiSenzaCosto} pacchetti senza offerta scelta e {t.VociNonAssegnate} voci non assegnate).");

        if (f.SpeseGenerali.Count > 0)
        {
            Titoletto(sec, "Spese generali");
            tab = Tabella(sec, ("Descrizione", 8, false), ("Calcolo", 6, false), ("Importo", 4, true));
            foreach (var s in f.SpeseGenerali)
                Riga(tab, s.Descrizione,
                    s.Modo == Dominio.ModoSpesaGenerale.Importo ? "Importo" : $"{Formato.Percento(s.Percentuale)} su {Testi.BaseSpesa(s.Base).ToLowerInvariant()}",
                    Formato.Euro(s.ImportoCalcolato));
            RigaTotale(tab, "Totale", "", Formato.Euro(t.SpeseGenerali));
        }

        if (f.Integrazioni.Count > 0)
        {
            Titoletto(sec, "Integrazioni di ricavo");
            tab = Tabella(sec, ("Descrizione", 8, false), ("Pacchetto", 6, false), ("Importo", 4, true));
            foreach (var i in f.Integrazioni) Riga(tab, i.Descrizione, i.Pacchetto ?? "Commessa", Formato.Euro(i.Importo));
        }

        Titoletto(sec, "Pacchetti");
        tab = Tabella(sec, ("N.", 0.7, false), ("Pacchetto", 5.1, false), ("Fornitore", 3.2, false), ("Stato", 1.8, false),
            ("Ricavo", 2.1, true), ("Costo", 2.1, true), ("MDC", 2.0, true), ("MDC %", 1.0, true));
        tab.Format.Font.Size = 7.5;
        foreach (var p in f.Pacchetti)
        {
            var r = Riga(tab, p.Ordine.ToString(), p.Descrizione, p.Fornitore ?? "—", Testi.Stato(p.Stato),
                Formato.Euro(p.Ricavo), p.Costo is null ? "—" : Formato.Euro(p.Costo), p.Mdc is null ? "—" : Formato.Euro(p.Mdc),
                p.MarginePercentuale is null ? "" : Formato.Numero(p.MarginePercentuale));
            if (p.Mdc < 0) r.Cells[6].Format.Font.Color = Colore(Stile.Negativo);
        }
        var pacchetti = f.Pacchetti;
        var ricavoConCosto = pacchetti.Where(p => p.Costo is not null).Sum(p => p.Ricavo);
        var costo = pacchetti.Sum(p => p.Costo ?? 0);
        RigaTotale(tab, "", "Totale", "", "", Formato.Euro(pacchetti.Sum(p => p.Ricavo)), Formato.Euro(costo),
            Formato.Euro(ricavoConCosto - costo), Formato.Numero(Importi.MargineSulRicavo(ricavoConCosto, costo)));
        Nota(sec, "MDC e MDC % del totale sono calcolati sui soli pacchetti con un costo pianificato.");

        if (avvisi is { Count: > 0 })
        {
            Titoletto(sec, "Avvisi");
            foreach (var a in avvisi)
            {
                var par = sec.AddParagraph();
                par.Format.SpaceAfter = Unit.FromPoint(2);
                par.AddFormattedText(a.Gravita switch
                {
                    GravitaAvviso.Errore => "Errore: ",
                    GravitaAvviso.Attenzione => "Attenzione: ",
                    _ => "Nota: ",
                }, TextFormat.Bold);
                par.AddText(a.Problema + " " + a.AzioneSuggerita);
            }
        }

        return Rendi(doc);
    }

    public static byte[] Pacchetto(IntestazioneStampa intestazione, DettaglioPacchetto d)
    {
        var p = d.Pacchetto;
        var (doc, sec) = Documento(intestazione, $"Scheda pacchetto: {p.Descrizione}", orizzontale: true);
        var scelta = d.Scelta;

        var info = Tabella(sec, ("", 4, false), ("", 9.35, false), ("", 4, false), ("", 9.35, false), intestazioneColonne: false);
        Riga(info, "Affidamento", Testi.Affidamento(p.Tipo), "Stato del costo", Testi.Stato(p.StatoCosto));
        Riga(info, "Codice / ODA", $"{p.Codice ?? "—"} / {p.RiferimentoOda ?? "—"}", "Ribasso d'asta", Formato.Percento(d.RibassoPercentuale));
        Riga(info, "Offerta scelta", scelta is null ? "Nessuna" : $"{scelta.Offerta.NomeFornitore} – {Testi.Offerta(scelta.Offerta.Tipo)}",
            "Riferimento offerta", scelta is null ? "—" : Riferimento(scelta.Offerta));
        foreach (var c in info.Rows.Cast<Row>().SelectMany(r => new[] { r.Cells[0], r.Cells[2] })) c.Format.Font.Bold = true;

        if (scelta is null)
        {
            Titoletto(sec, "Voci del pacchetto (ricavo da computo, costo non pianificato)");
            var tv = Tabella(sec, ("N.", 0.7, false), ("Tariffa", 2.4, false), ("Descrizione", 13, false), ("U.M.", 1.2, false),
                ("Quantità", 2.2, true), ("Prezzo", 2.2, true), ("Ricavo netto", 3, true));
            tv.Format.Font.Size = 7.5;
            var n = 0;
            decimal totale = 0;
            foreach (var v in d.Voci)
            {
                var r = CalcoloRicavi.Voce(v.Quantita, v.Prezzo, d.RibassoPercentuale, v.PercentualeNonSoggetta);
                totale += r.Netto;
                Riga(tv, (++n).ToString(), v.Tariffa ?? "", Breve(v.Descrizione), v.Unita ?? "", Formato.Numero(v.Quantita),
                    Formato.Euro(v.Prezzo), Formato.Euro(r.Netto));
            }
            RigaTotale(tv, "", "", "Totale", "", "", "", Formato.Euro(totale));
        }
        else
        {
            var o = scelta.Offerta;
            var perVoce = o.Voci.ToDictionary(v => v.VoceComputoId);
            Titoletto(sec, "Pianificazione delle voci");
            var tv = Tabella(sec, ("N.", 0.6, false), ("Tariffa", 2.0, false), ("Descrizione", 5.6, false), ("U.M.", 0.9, false),
                ("Quantità", 1.6, true), ("Prezzo", 1.7, true), ("Prezzo rib.", 1.7, true), ("Ricavo netto", 2.0, true),
                ("Fornitura", 1.5, true), ("F. e posa", 1.5, true), ("M.O. e noli", 1.5, true), ("Costo unit.", 1.6, true),
                ("Costo", 1.9, true), ("MDC", 2.0, true));
            tv.Format.Font.Size = 7;
            var n = 0;
            foreach (var v in scelta.Risultato.Voci)
            {
                perVoce.TryGetValue(v.Voce.Id, out var ov);
                var r = Riga(tv, (++n).ToString(), v.Voce.Tariffa ?? "",
                    Breve(v.Voce.Descrizione) + (v.QuantitaModificata ? $"\n(quantità di computo {Formato.Numero(v.Voce.Quantita)}: {v.NotaQuantita})" : ""),
                    v.Voce.Unita ?? "", Formato.Numero(v.Quantita), Formato.Euro(v.Voce.Prezzo), Formato.Numero(v.Ricavo.PrezzoRibassato, 4),
                    Formato.Euro(v.Ricavo.Netto),
                    Importo(ov?.Fornitura), Importo(ov?.FornituraEPosa), Importo(ov?.ManodoperaENoli),
                    v.CostoUnitario is null ? "" : Formato.Euro(v.CostoUnitario), Formato.Euro(v.Costo), Formato.Euro(v.Mdc));
                if (v.Mdc < 0) r.Cells[13].Format.Font.Color = Colore(Stile.Negativo);
            }
            var ris = scelta.Risultato;
            RigaTotale(tv, "", "", "Totale voci", "", "", "", "", Formato.Euro(ris.RicavoNetto), "", "", "", "",
                Formato.Euro(ris.CostoVoci), Formato.Euro(ris.RicavoNetto - ris.CostoVoci));

            var corpo = o.CostiACorpo.OrderBy(c => c.Ordine).ToList();
            var idVoci = d.Voci.Select(v => v.Id).ToHashSet();
            var righeLibere = o.Righe.Where(r => r.VoceComputoId is null || !idVoci.Contains(r.VoceComputoId.Value)).OrderBy(r => r.Ordine).ToList();
            if (corpo.Count > 0 || righeLibere.Count > 0)
            {
                Titoletto(sec, "Costi a corpo e righe d'offerta non assegnate a voci");
                var tc = Tabella(sec, ("Descrizione", 20.7, false), ("Nota", 3, false), ("Importo", 3, true));
                tc.Format.Font.Size = 7.5;
                foreach (var c in corpo)
                    Riga(tc, c.Descrizione, c.Incluso ? "incluso" : "", c.Incluso ? Formato.Euro(0) : Formato.Euro(c.Importo));
                foreach (var r in righeLibere)
                    Riga(tc, $"{r.CodiceArticolo} {r.Descrizione} – {Formato.Numero(r.Quantita)} {r.Unita} × {Formato.Euro(r.PrezzoListino)}" +
                             (r.ScontoPercentuale != 0 ? $" sconto {Formato.Percento(r.ScontoPercentuale)}" : ""),
                        "riga d'offerta", Formato.Euro(CalcoloPacchetti.ImportoRiga(r.Quantita, r.PrezzoListino, r.ScontoPercentuale)));
            }
        }

        Titoletto(sec, d.Offerte.Count > 1 ? "Confronto delle offerte" : "Riepilogo");
        var colonne = new List<(string, double, bool)> { ("", 5.2, false) };
        var larghezza = Math.Min(4.3, 21.5 / Math.Max(1, d.Offerte.Count));
        colonne.AddRange(d.Offerte.Select(x => ((x.Scelta ? "Scelta: " : "") + (x.Offerta.NomeFornitore ?? Testi.Offerta(x.Offerta.Tipo)), larghezza, true)));
        if (d.Offerte.Count == 0) colonne.Add(("Da computo", 4.3, true));
        var tr = Tabella(sec, colonne.ToArray());
        if (d.Offerte.Count == 0)
        {
            var (_, netto) = CalcoloPacchetti.RicavoDaComputo(d.Voci, d.RibassoPercentuale);
            Riga(tr, "Ricavo netto", Formato.Euro(netto));
            Riga(tr, "Costo", "non pianificato");
        }
        else
        {
            Riga(tr, ["Tipo di offerta", .. d.Offerte.Select(x => Testi.Offerta(x.Offerta.Tipo))]);
            Riga(tr, ["Ricavo netto", .. d.Offerte.Select(x => Formato.Euro(x.Risultato.RicavoNetto))]);
            Riga(tr, ["Costo", .. d.Offerte.Select(x => Formato.Euro(x.Risultato.Costo))]);
            RigaTotale(tr, ["MDC", .. d.Offerte.Select(x => Formato.Euro(x.Risultato.Mdc))]);
            Riga(tr, ["MDC % sui ricavi", .. d.Offerte.Select(x => Formato.Percento(x.Risultato.MarginePercentuale))]);
            Riga(tr, ["Ricarico % sui costi", .. d.Offerte.Select(x => Formato.Percento(x.Risultato.RicaricoPercentuale))]);
        }

        if (d.Note.Count > 0 || !string.IsNullOrWhiteSpace(p.Note))
        {
            Titoletto(sec, "Note");
            if (!string.IsNullOrWhiteSpace(p.Note)) Nota(sec, p.Note);
            foreach (var nota in d.Note)
            {
                var par = sec.AddParagraph();
                par.AddFormattedText($"{Formato.Data(nota.Data)} {nota.Iniziali} – ", TextFormat.Bold);
                par.AddText(nota.Testo);
            }
        }

        return Rendi(doc);
    }

    /// <summary>Ordine di acquisto al fornitore, A4 verticale, con l'intestazione dell'impresa.</summary>
    public static byte[] Ordine(DatiAzienda azienda, Dominio.OrdineAcquisto o, string commessa)
    {
        var (doc, sec) = Pagina($"Ordine {o.NumeroVisibile}", orizzontale: false);

        // Intestazione: impresa a sinistra, estremi dell'ordine a destra.
        var testa = sec.AddTable();
        testa.AddColumn(Unit.FromCentimeter(10.5));
        testa.AddColumn(Unit.FromCentimeter(7.5)).Format.Alignment = ParagraphAlignment.Right;
        var rt = testa.AddRow();
        var righeAzienda = azienda.Intestazione().ToList();
        if (righeAzienda.Count == 0) righeAzienda.Add("Dati dell'impresa non compilati (Amministrazione → Dati dell'impresa)");
        for (var i = 0; i < righeAzienda.Count; i++)
        {
            var par = rt.Cells[0].AddParagraph(righeAzienda[i]);
            if (i == 0) { par.Format.Font.Size = 12; par.Format.Font.Bold = true; par.Format.Font.Color = Colore(Stile.Primario); }
            else par.Format.Font.Size = 8;
        }
        var tit = rt.Cells[1].AddParagraph("ORDINE DI ACQUISTO");
        tit.Format.Font.Size = 13;
        tit.Format.Font.Bold = true;
        tit.Format.Font.Color = Colore(Stile.Primario);
        var num = rt.Cells[1].AddParagraph(o.Stato == Dominio.StatoOrdine.Bozza ? $"Bozza del {Formato.Data(o.Data)}" : $"{o.Numero} del {Formato.Data(o.Data)}");
        num.Format.Font.Size = 11;
        num.Format.Font.Bold = true;
        rt.Borders.Bottom.Width = Unit.FromPoint(0.75);
        rt.Borders.Bottom.Color = Colore(Stile.Primario);
        rt.BottomPadding = Unit.FromPoint(6);

        if (o.Stato == Dominio.StatoOrdine.Bozza)
        {
            var bozza = sec.AddParagraph("BOZZA – anteprima non valida come ordine: il numero si assegna all'emissione.");
            bozza.Format.Font.Size = 11;
            bozza.Format.Font.Bold = true;
            bozza.Format.Font.Color = Colore(Stile.Negativo);
            bozza.Format.SpaceBefore = Unit.FromPoint(6);
        }
        if (o.Stato == Dominio.StatoOrdine.Annullato)
        {
            var ann = sec.AddParagraph($"ORDINE ANNULLATO il {Formato.Data(o.AnnullatoIl)}: {o.MotivoAnnullamento}");
            ann.Format.Font.Size = 11;
            ann.Format.Font.Bold = true;
            ann.Format.Font.Color = Colore(Stile.Negativo);
            ann.Format.SpaceBefore = Unit.FromPoint(6);
        }

        // Destinatario e riferimenti.
        var dest = sec.AddTable();
        dest.TopPadding = Unit.FromPoint(8);
        dest.AddColumn(Unit.FromCentimeter(9));
        dest.AddColumn(Unit.FromCentimeter(9));
        var rd = dest.AddRow();
        rd.Cells[0].AddParagraph("Spett.le").Format.Font.Color = Colore(Stile.TestoSecondario);
        var f = rd.Cells[0].AddParagraph(o.Fornitore);
        f.Format.Font.Bold = true;
        f.Format.Font.Size = 10;
        foreach (var l in (o.DatiFornitore ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            rd.Cells[0].AddParagraph(l).Format.Font.Size = 8.5;
        void Rif(string etichetta, string? valore)
        {
            if (string.IsNullOrWhiteSpace(valore)) return;
            var par = rd.Cells[1].AddParagraph();
            par.AddFormattedText(etichetta + ": ", TextFormat.Bold);
            par.AddText(valore);
            par.Format.SpaceAfter = Unit.FromPoint(1.5);
        }
        Rif("Commessa", commessa);
        Rif("Riferimento", o.RiferimentoOfferta);

        var ogg = sec.AddParagraph();
        ogg.Format.SpaceBefore = Unit.FromPoint(10);
        ogg.Format.SpaceAfter = Unit.FromPoint(6);
        ogg.AddFormattedText("Oggetto: ", TextFormat.Bold);
        ogg.AddText(o.Oggetto);
        sec.AddParagraph("Con la presente Vi ordiniamo quanto segue, alle condizioni indicate:").Format.SpaceAfter = Unit.FromPoint(4);

        var tab = Tabella(sec, ("N.", 0.7, false), ("Codice", 2.1, false), ("Descrizione", 7.2, false), ("U.M.", 1.5, false),
            ("Quantità", 1.8, true), ("Prezzo", 2.1, true), ("Importo", 2.6, true));
        tab.Format.Font.Size = 8;
        foreach (var r in o.Righe.OrderBy(r => r.Ordine))
            Riga(tab, r.Ordine.ToString(), r.Codice ?? "", Breve(r.Descrizione, 600), r.Unita ?? "", Formato.Numero(r.Quantita),
                Formato.Numero(r.Prezzo, r.Prezzo == Math.Round(r.Prezzo, 2) ? 2 : 4), Formato.Euro(r.Importo));
        RigaTotale(tab, "", "", "Totale imponibile (IVA esclusa)", "", "", "", Formato.Euro(o.Totale));

        Titoletto(sec, "Condizioni");
        var cond = Tabella(sec, [("", 4.5, false), ("", 13.5, false)], intestazioneColonne: false);
        void Condizione(string etichetta, string? valore)
        {
            var r = Riga(cond, etichetta, string.IsNullOrWhiteSpace(valore) ? "—" : valore);
            r.Cells[0].Format.Font.Bold = true;
        }
        Condizione("Pagamento", o.Pagamento);
        Condizione("Consegna", o.Consegna);
        Condizione("Luogo di consegna", o.LuogoConsegna);
        if (!string.IsNullOrWhiteSpace(o.Note)) Condizione("Note", o.Note);

        if (!string.IsNullOrWhiteSpace(o.Condizioni))
        {
            Titoletto(sec, "Condizioni generali");
            foreach (var l in o.Condizioni.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var par = sec.AddParagraph(l);
                par.Format.Font.Size = 7.5;
                par.Format.SpaceAfter = Unit.FromPoint(1.5);
            }
        }

        var firme = sec.AddTable();
        firme.KeepTogether = true;
        firme.AddColumn(Unit.FromCentimeter(9)).Format.Alignment = ParagraphAlignment.Center;
        firme.AddColumn(Unit.FromCentimeter(9)).Format.Alignment = ParagraphAlignment.Center;
        var rf = firme.AddRow();
        rf.TopPadding = Unit.FromPoint(24);
        rf.Cells[0].AddParagraph(azienda.Compilati ? $"Per {azienda.RagioneSociale}" : "Per l'impresa");
        rf.Cells[1].AddParagraph("Per accettazione (timbro e firma del fornitore)");
        var linea = firme.AddRow();
        linea.TopPadding = Unit.FromPoint(28);
        foreach (var cella in new[] { linea.Cells[0], linea.Cells[1] })
        {
            var par = cella.AddParagraph("______________________________");
            par.Format.Font.Color = Colore(Stile.TestoSecondario);
        }
        var nota = sec.AddParagraph("Si prega di restituire copia del presente ordine firmata per accettazione. " +
                                    "Citare il numero d'ordine su documenti di trasporto e fatture.");
        nota.Format.Font.Size = 7.5;
        nota.Format.Font.Italic = true;
        nota.Format.SpaceBefore = Unit.FromPoint(10);

        var piede = sec.Footers.Primary.AddParagraph();
        piede.Format.Font.Size = 7.5;
        piede.Format.Font.Color = Colore(Stile.TestoSecondario);
        piede.AddText($"Ordine {o.NumeroVisibile} del {Formato.Data(o.Data)} – {o.Fornitore} – pagina ");
        piede.AddPageField();
        piede.AddText(" di ");
        piede.AddNumPagesField();
        return Rendi(doc);
    }

    // ---- Struttura comune ----

    private static (Document, Section) Documento(IntestazioneStampa intestazione, string titolo, bool orizzontale)
    {
        var (doc, sec) = Pagina(titolo, orizzontale);

        var t = sec.AddParagraph(titolo);
        t.Format.Font.Size = 14;
        t.Format.Font.Bold = true;
        t.Format.Font.Color = Colore(Stile.Primario);
        var c = sec.AddParagraph(intestazione.Commessa);
        c.Format.Font.Bold = true;
        c.Format.SpaceBefore = Unit.FromPoint(2);
        var s = sec.AddParagraph(intestazione.Sottotitolo);
        s.Format.Font.Color = Colore(Stile.TestoSecondario);
        s.Format.SpaceAfter = Unit.FromPoint(6);
        s.Format.Borders.Bottom.Width = Unit.FromPoint(0.75);
        s.Format.Borders.Bottom.Color = Colore(Stile.Primario);

        var piede = sec.Footers.Primary.AddParagraph();
        piede.Format.Font.Size = 7.5;
        piede.Format.Font.Color = Colore(Stile.TestoSecondario);
        piede.AddText($"Corico – {intestazione.Commessa} – stampato il {DateTime.Now:dd/MM/yyyy HH:mm} – pagina ");
        piede.AddPageField();
        piede.AddText(" di ");
        piede.AddNumPagesField();
        return (doc, sec);
    }

    private static (Document, Section) Pagina(string titolo, bool orizzontale)
    {
        var doc = new Document();
        doc.Info.Title = titolo;
        doc.Info.Author = "Corico";
        var normale = doc.Styles[StyleNames.Normal]!;
        normale.Font.Name = Carattere;
        normale.Font.Size = 9;

        var sec = doc.AddSection();
        sec.PageSetup = doc.DefaultPageSetup.Clone();
        // Dimensioni esplicite: con PDFsharp 6 l'orientamento da solo non ruota il formato A4.
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.Orientation = Orientation.Portrait;
        sec.PageSetup.PageWidth = Unit.FromCentimeter(orizzontale ? 29.7 : 21);
        sec.PageSetup.PageHeight = Unit.FromCentimeter(orizzontale ? 21 : 29.7);
        sec.PageSetup.LeftMargin = sec.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        sec.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        sec.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);
        sec.PageSetup.FooterDistance = Unit.FromCentimeter(0.7);
        return (doc, sec);
    }

    private static void Titoletto(Section sec, string testo)
    {
        var p = sec.AddParagraph(testo);
        p.Format.Font.Size = 10.5;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = Colore(Stile.Primario);
        p.Format.SpaceBefore = Unit.FromPoint(10);
        p.Format.SpaceAfter = Unit.FromPoint(4);
        p.Format.KeepWithNext = true;
    }

    private static void Nota(Section sec, string testo)
    {
        var p = sec.AddParagraph(testo);
        p.Format.Font.Italic = true;
        p.Format.Font.Size = 8;
        p.Format.SpaceBefore = Unit.FromPoint(3);
    }

    private static Table Tabella(Section sec, params (string Titolo, double Cm, bool Numero)[] colonne) =>
        Tabella(sec, colonne, true);

    private static Table Tabella(Section sec, (string Titolo, double Cm, bool Numero) c1, (string, double, bool) c2,
        (string, double, bool) c3, (string, double, bool) c4, bool intestazioneColonne) =>
        Tabella(sec, [c1, c2, c3, c4], intestazioneColonne);

    private static Table Tabella(Section sec, (string Titolo, double Cm, bool Numero)[] colonne, bool intestazioneColonne)
    {
        var t = sec.AddTable();
        t.Borders.Bottom.Width = Unit.FromPoint(0.25);
        t.Borders.Bottom.Color = Colore(Stile.Bordo);
        t.TopPadding = t.BottomPadding = Unit.FromPoint(1.5);
        t.LeftPadding = t.RightPadding = Unit.FromPoint(3);
        foreach (var (_, cm, numero) in colonne)
            t.AddColumn(Unit.FromCentimeter(cm)).Format.Alignment = numero ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        if (intestazioneColonne)
        {
            var r = t.AddRow();
            r.HeadingFormat = true;
            r.Shading.Color = Colore(Stile.Primario);
            r.Format.Font.Bold = true;
            r.Format.Font.Color = Colors.White;
            r.VerticalAlignment = VerticalAlignment.Center;
            for (var i = 0; i < colonne.Length; i++) r.Cells[i].AddParagraph(colonne[i].Titolo);
        }
        return t;
    }

    private static Row Riga(Table t, params string[] valori)
    {
        var r = t.AddRow();
        var dati = t.Rows.Cast<Row>().Count(x => !x.HeadingFormat);
        if (dati % 2 == 0) r.Shading.Color = Colore(Stile.RigaAlterna);
        for (var i = 0; i < valori.Length; i++) r.Cells[i].AddParagraph(valori[i]);
        return r;
    }

    private static Row RigaTotale(Table t, params string[] valori)
    {
        var r = Riga(t, valori);
        r.Shading.Color = Colore(Stile.Totale);
        r.Format.Font.Bold = true;
        r.Borders.Top.Width = Unit.FromPoint(0.75);
        r.Borders.Top.Color = Colore(Stile.Primario);
        return r;
    }

    private static string Riferimento(Dominio.Offerta o) =>
        string.Join(" ", new[]
        {
            o.Numero is null ? null : "n. " + o.Numero,
            o.Revisione is null ? null : "rev. " + o.Revisione,
            o.Data is null ? null : "del " + Formato.Data(o.Data),
            o.Scadenza is null ? null : "(scade il " + Formato.Data(o.Scadenza) + ")",
        }.Where(x => x is not null)) is { Length: > 0 } s ? s : "—";

    private static string Importo(decimal? v) => v is null or 0 ? "" : Formato.Euro(v);

    private static string Breve(string testo, int massimo = 220)
    {
        var t = testo.ReplaceLineEndings(" ").Trim();
        return t.Length <= massimo ? t : t[..(massimo - 3)].TrimEnd() + "…";
    }

    private static Color Colore(string esadecimale)
    {
        var h = esadecimale.TrimStart('#');
        return new Color(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }

    private static byte[] Rendi(Document doc)
    {
        var r = new PdfDocumentRenderer { Document = doc };
        r.RenderDocument();
        using var ms = new MemoryStream();
        r.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }
}

/// <summary>
/// Caratteri delle stampe: Arial su Windows; in alternativa Liberation Sans o DejaVu Sans
/// (ambiente di sviluppo e test). Registrato una sola volta per processo.
/// </summary>
internal sealed class CaratteriStampa : IFontResolver
{
    public const string Famiglia = "CoricoStampa";
    private static readonly object Blocco = new();

    private static IEnumerable<string[]> Candidati()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(fonts))
            yield return new[] { "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf" }.Select(f => Path.Combine(fonts, f)).ToArray();
        const string liberation = "/usr/share/fonts/truetype/liberation/LiberationSans-";
        yield return [liberation + "Regular.ttf", liberation + "Bold.ttf", liberation + "Italic.ttf", liberation + "BoldItalic.ttf"];
        const string dejavu = "/usr/share/fonts/truetype/dejavu/DejaVuSans";
        yield return [dejavu + ".ttf", dejavu + "-Bold.ttf", dejavu + "-Oblique.ttf", dejavu + "-BoldOblique.ttf"];
    }

    private readonly string[] _file;

    private CaratteriStampa(string[] file) => _file = file;

    public static void Registra()
    {
        lock (Blocco)
        {
            if (GlobalFontSettings.FontResolver is CaratteriStampa) return;
            var scelta = Candidati().FirstOrDefault(c => File.Exists(c[0]))
                ?? throw new InvalidOperationException("Nessun carattere disponibile per le stampe PDF (Arial, Liberation Sans o DejaVu Sans).");
            GlobalFontSettings.FontResolver = new CaratteriStampa(scelta);
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var indice = (isBold ? 1 : 0) + (isItalic ? 2 : 0);
        // Se manca la variante corsiva si usa la normale con simulazione.
        if (!File.Exists(_file[indice])) indice = isBold ? 1 : 0;
        return new FontResolverInfo("CoricoStampa#" + indice, isBold && indice % 2 == 0, isItalic && indice < 2);
    }

    public byte[]? GetFont(string faceName) => File.ReadAllBytes(_file[int.Parse(faceName[(faceName.IndexOf('#') + 1)..])]);
}
