using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Interfaccia;
using Corico.Modules.Preventivo.Servizi;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace Corico.Modules.Preventivo.Esportazioni;

public static partial class EsportazionePdf
{
    /// <summary>
    /// Comunicazione dell'esito della qualificazione a DL, RUP e CSE, A4 verticale con l'intestazione dell'impresa;
    /// in coda, se richiesti, l'attestato di qualificazione e l'elenco dei documenti verificati.
    /// </summary>
    public static byte[] ComunicazioneQualifica(LetteraQualifica l)
    {
        var c = l.Comunicazione;
        var q = l.Qualifica;
        var bozza = c.Stato == StatoComunicazione.Bozza;
        var (doc, sec) = Pagina($"Comunicazione {c.NumeroVisibile}", orizzontale: false);
        IntestazioneLettera(sec, l.Azienda, "COMUNICAZIONE", bozza ? $"Bozza del {Formato.Data(c.DataEmissione)}" : $"{c.Numero} del {Formato.Data(c.DataEmissione)}");

        if (bozza) Avviso(sec, "BOZZA – anteprima non valida come comunicazione: il numero si assegna con il PDF definitivo.");
        if (c.Stato == StatoComunicazione.Annullata) Avviso(sec, $"COMUNICAZIONE ANNULLATA il {Formato.Data(c.AnnullataIl)}: {c.MotivoAnnullamento}");

        // Destinatari: uno per riga, con ruolo, ente e recapiti.
        var dest = sec.AddTable();
        dest.TopPadding = Unit.FromPoint(8);
        dest.AddColumn(Unit.FromCentimeter(8.5));
        dest.AddColumn(Unit.FromCentimeter(9.5));
        var rd = dest.AddRow();
        rd.Cells[0].AddParagraph("Riferimenti").Format.Font.Color = Colore(Stile.TestoSecondario);
        void Rif(string etichetta, string? valore)
        {
            if (string.IsNullOrWhiteSpace(valore)) return;
            var par = rd.Cells[0].AddParagraph();
            par.AddFormattedText(etichetta + ": ", TextFormat.Bold);
            par.AddText(valore);
            par.Format.SpaceAfter = Unit.FromPoint(1.5);
        }
        Rif("Commessa", $"{l.Commessa.Codice}{(string.IsNullOrWhiteSpace(l.Commessa.CodiceEsterno) ? "" : $" (rif. {l.Commessa.CodiceEsterno})")} – {l.Commessa.Descrizione}");
        Rif("Committente", l.Committente);
        Rif("Contratto", l.Commessa.ContrattoNumero is { Length: > 0 } n ? $"n. {n}{(l.Commessa.ContrattoData is { } dc ? $" del {Formato.Data(dc)}" : "")}" : null);
        Rif("Cantiere", string.Join(" ", new[] { l.Commessa.IndirizzoCantiere, l.Commessa.ComuneCantiere,
            l.Commessa.ProvinciaCantiere is { Length: > 0 } pr ? $"({pr})" : null }.Where(x => !string.IsNullOrWhiteSpace(x))));

        var scelti = c.Destinatari.Where(d => d.Selezionato).OrderBy(d => Array.IndexOf(QualificheService.RuoliDestinatari, d.Ruolo)).ToList();
        foreach (var d in scelti)
        {
            var ruolo = rd.Cells[1].AddParagraph($"Al {Testi.RuoloLettera(d.Ruolo)} ({RuoliCommessa.Sigla(d.Ruolo)})");
            ruolo.Format.Font.Color = Colore(Stile.TestoSecondario);
            ruolo.Format.Font.Size = 8;
            var nome = rd.Cells[1].AddParagraph(QualificheService.Nominativo(d.Titolo, d.Nominativo));
            nome.Format.Font.Bold = true;
            foreach (var riga in new[] { d.Ente, d.Indirizzo, d.Pec is { Length: > 0 } pec ? "PEC " + pec : null, d.Email }.Where(x => !string.IsNullOrWhiteSpace(x)))
                rd.Cells[1].AddParagraph(riga!).Format.Font.Size = 8.5;
            rd.Cells[1].AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
        }

        var ogg = sec.AddParagraph();
        ogg.Format.SpaceBefore = Unit.FromPoint(10);
        ogg.Format.SpaceAfter = Unit.FromPoint(6);
        ogg.AddFormattedText("Oggetto: ", TextFormat.Bold);
        ogg.AddFormattedText(c.Oggetto, TextFormat.Bold);

        Paragrafo(sec, $"Con la presente si comunica che l'operatore economico di seguito indicato, in qualità di {Testi.TipoOperatore(q.Tipo).ToLowerInvariant()} " +
                       "nell'ambito della commessa in oggetto, è stato sottoposto al processo di qualificazione interna aziendale, " +
                       "conclusosi con esito positivo.");

        Titoletto(sec, "Operatore economico e qualificazione");
        var dati = Tabella(sec, [("", 5, false), ("", 13, false)], intestazioneColonne: false);
        void Dato(string etichetta, string? valore)
        {
            var r = Riga(dati, etichetta, string.IsNullOrWhiteSpace(valore) ? "—" : valore);
            r.Cells[0].Format.Font.Bold = true;
        }
        Dato("Ragione sociale", l.Fornitore?.Denominazione ?? q.Fornitore);
        if (l.Fornitore is { } f)
        {
            Dato("Sede", string.Join(" ", new[] { f.Indirizzo, f.Comune, f.Provincia is { Length: > 0 } p ? $"({p})" : null }.Where(x => !string.IsNullOrWhiteSpace(x))));
            Dato("Partita IVA", f.PartitaIva);
            Dato("Codice fiscale", f.CodiceFiscale);
        }
        Dato("Tipologia di operatore", Testi.TipoOperatore(q.Tipo));
        if (!string.IsNullOrWhiteSpace(q.OggettoPrestazioni)) Dato("Oggetto delle prestazioni", q.OggettoPrestazioni);
        Dato("Data di conclusione", Formato.Data(q.DataConclusione));
        var esito = Riga(dati, "Esito", "POSITIVO – operatore qualificato");
        esito.Cells[0].Format.Font.Bold = true;
        esito.Cells[1].Format.Font.Bold = true;
        Dato("Qualificazione curata da", $"{q.DirettoreCantiere}, Direttore di cantiere");

        Titoletto(sec, "Documentazione verificata");
        var verificati = q.Documenti.OrderBy(d => d.Ordine).ToList();
        if (verificati.Count > 0)
            foreach (var d in verificati)
            {
                var par = sec.AddParagraph("• " + d.Descrizione + (d.Scadenza is { } s ? $" (validità fino al {Formato.Data(s)})" : ""));
                par.Format.LeftIndent = Unit.FromCentimeter(0.3);
                par.Format.Font.Size = 8.5;
            }
        Paragrafo(sec, $"La documentazione è conservata nel fascicolo del fornitore{(string.IsNullOrWhiteSpace(q.RiferimentoFascicolo) ? "" : $" (rif. {q.RiferimentoFascicolo})")} " +
                       "ed è disponibile per la verifica su richiesta.");

        Titoletto(sec, "Limitazioni e prescrizioni");
        Paragrafo(sec, string.IsNullOrWhiteSpace(c.Prescrizioni) ? "Nessuna." : c.Prescrizioni);

        if (!string.IsNullOrWhiteSpace(c.Note))
        {
            Titoletto(sec, "Note");
            Paragrafo(sec, c.Note);
        }

        var avvertenza = Paragrafo(sec, "La presente comunicazione riguarda esclusivamente l'esito della qualificazione interna aziendale e non sostituisce " +
                                        "l'autorizzazione al subappalto né gli altri adempimenti prescritti dalla normativa vigente o dalla documentazione contrattuale, " +
                                        "che restano gestiti separatamente.");
        avvertenza.Format.Font.Italic = true;
        avvertenza.Format.SpaceBefore = Unit.FromPoint(8);
        Paragrafo(sec, "Restando a disposizione per ogni chiarimento, si porgono distinti saluti.");

        var allegati = new List<string>();
        if (c.AllegaAttestato) allegati.Add("Attestato di qualificazione del fornitore");
        if (c.AllegaElencoDocumenti) allegati.Add("Elenco dei documenti verificati");
        allegati.AddRange(c.Allegati.OrderBy(a => a.Ordine).Select(a => a.Descrizione));
        if (allegati.Count > 0)
        {
            Titoletto(sec, "Allegati");
            for (var i = 0; i < allegati.Count; i++)
                sec.AddParagraph($"{i + 1}. {allegati[i]}").Format.Font.Size = 8.5;
        }

        Firme(sec, l.Azienda, q.DirettoreCantiere);
        Piede(sec, $"Comunicazione {c.NumeroVisibile} del {Formato.Data(c.DataEmissione)} – {q.Fornitore}");

        if (c.AllegaAttestato) Attestato(doc, l, allegati.IndexOf("Attestato di qualificazione del fornitore") + 1);
        if (c.AllegaElencoDocumenti) ElencoVerificati(doc, l, allegati.IndexOf("Elenco dei documenti verificati") + 1);
        return Rendi(doc);
    }

    private static void Attestato(Document doc, LetteraQualifica l, int numero)
    {
        var q = l.Qualifica;
        var sec = NuovaSezione(doc);
        IntestazioneLettera(sec, l.Azienda, $"ALLEGATO {numero}", "Attestato di qualificazione");
        var t = sec.AddParagraph("ATTESTATO DI QUALIFICAZIONE DEL FORNITORE");
        t.Format.Alignment = ParagraphAlignment.Center;
        t.Format.Font.Size = 13;
        t.Format.Font.Bold = true;
        t.Format.Font.Color = Colore(Stile.Primario);
        t.Format.SpaceBefore = Unit.FromPoint(18);
        t.Format.SpaceAfter = Unit.FromPoint(12);

        var impresa = l.Azienda.Compilati ? "da " + l.Azienda.RagioneSociale : "dall'impresa";
        Paragrafo(sec, $"Si attesta che l'operatore economico {l.Fornitore?.Denominazione ?? q.Fornitore}" +
                       (l.Fornitore is { PartitaIva.Length: > 0 } f ? $", partita IVA {f.PartitaIva}" : "") +
                       $", è stato qualificato {impresa} in qualità di {Testi.TipoOperatore(q.Tipo).ToLowerInvariant()} per la commessa " +
                       $"{l.Commessa.Codice} – {l.Commessa.Descrizione}" +
                       (string.IsNullOrWhiteSpace(q.OggettoPrestazioni) ? "" : $", per le seguenti prestazioni: {q.OggettoPrestazioni}") + ".");
        Paragrafo(sec, $"La qualificazione, curata dal Direttore di cantiere {q.DirettoreCantiere}, si è conclusa il {Formato.Data(q.DataConclusione)} " +
                       "con esito positivo, sulla base della documentazione elencata nell'allegato dedicato e conservata nel fascicolo del fornitore.");
        Paragrafo(sec, "Limitazioni e prescrizioni: " + (string.IsNullOrWhiteSpace(l.Comunicazione.Prescrizioni) ? "nessuna." : l.Comunicazione.Prescrizioni));
        Paragrafo(sec, "Il presente attestato non sostituisce l'autorizzazione al subappalto né gli altri adempimenti prescritti dalla normativa " +
                       "o dalla documentazione contrattuale.").Format.Font.Italic = true;
        Firme(sec, l.Azienda, q.DirettoreCantiere);
        Piede(sec, $"Allegato {numero} alla comunicazione {l.Comunicazione.NumeroVisibile} – {q.Fornitore}");
    }

    private static void ElencoVerificati(Document doc, LetteraQualifica l, int numero)
    {
        var q = l.Qualifica;
        var sec = NuovaSezione(doc);
        IntestazioneLettera(sec, l.Azienda, $"ALLEGATO {numero}", "Elenco dei documenti verificati");
        Titoletto(sec, $"Documenti verificati – {q.Fornitore}");
        var tab = Tabella(sec, ("N.", 0.8, false), ("Documento", 11.2, false), ("Validità fino al", 3, false), ("Esito", 3, false));
        var n = 1;
        foreach (var d in q.Documenti.OrderBy(d => d.Ordine))
            Riga(tab, (n++).ToString(), d.Descrizione, d.Scadenza is null ? "—" : Formato.Data(d.Scadenza), d.Verificato ? "Verificato" : "Non verificato");
        Paragrafo(sec, $"Verifica conclusa il {Formato.Data(q.DataConclusione)} a cura del Direttore di cantiere {q.DirettoreCantiere}. " +
                       $"I documenti sono conservati nel fascicolo del fornitore{(string.IsNullOrWhiteSpace(q.RiferimentoFascicolo) ? "" : $" (rif. {q.RiferimentoFascicolo})")}.")
            .Format.SpaceBefore = Unit.FromPoint(8);
        Piede(sec, $"Allegato {numero} alla comunicazione {l.Comunicazione.NumeroVisibile} – {q.Fornitore}");
    }

    // ---- Parti comuni delle lettere ----

    /// <summary>Impresa a sinistra, titolo ed estremi a destra, con la linea di separazione (come ordini e richieste).</summary>
    private static void IntestazioneLettera(Section sec, DatiAzienda azienda, string titolo, string estremi)
    {
        var testa = sec.AddTable();
        testa.AddColumn(Unit.FromCentimeter(10.5));
        testa.AddColumn(Unit.FromCentimeter(7.5)).Format.Alignment = ParagraphAlignment.Right;
        var rt = testa.AddRow();
        var righe = azienda.Intestazione().ToList();
        if (righe.Count == 0) righe.Add("Dati dell'impresa non compilati (Amministrazione → Dati dell'impresa)");
        for (var i = 0; i < righe.Count; i++)
        {
            var par = rt.Cells[0].AddParagraph(righe[i]);
            if (i == 0) { par.Format.Font.Size = 12; par.Format.Font.Bold = true; par.Format.Font.Color = Colore(Stile.Primario); }
            else par.Format.Font.Size = 8;
        }
        var tit = rt.Cells[1].AddParagraph(titolo);
        tit.Format.Font.Size = 13;
        tit.Format.Font.Bold = true;
        tit.Format.Font.Color = Colore(Stile.Primario);
        var num = rt.Cells[1].AddParagraph(estremi);
        num.Format.Font.Size = 11;
        num.Format.Font.Bold = true;
        rt.Borders.Bottom.Width = Unit.FromPoint(0.75);
        rt.Borders.Bottom.Color = Colore(Stile.Primario);
        rt.BottomPadding = Unit.FromPoint(6);
    }

    private static void Avviso(Section sec, string testo)
    {
        var p = sec.AddParagraph(testo);
        p.Format.Font.Size = 11;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = Colore(Stile.Negativo);
        p.Format.SpaceBefore = Unit.FromPoint(6);
    }

    private static Paragraph Paragrafo(Section sec, string testo)
    {
        var p = sec.AddParagraph();
        var righe = testo.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < righe.Length; i++)
        {
            if (i > 0) p.AddLineBreak();
            p.AddText(righe[i]);
        }
        p.Format.Alignment = ParagraphAlignment.Justify;
        p.Format.SpaceAfter = Unit.FromPoint(4);
        return p;
    }

    private static void Firme(Section sec, DatiAzienda azienda, string? direttore)
    {
        var firme = sec.AddTable();
        firme.KeepTogether = true;
        firme.AddColumn(Unit.FromCentimeter(9)).Format.Alignment = ParagraphAlignment.Center;
        firme.AddColumn(Unit.FromCentimeter(9)).Format.Alignment = ParagraphAlignment.Center;
        var rf = firme.AddRow();
        rf.TopPadding = Unit.FromPoint(12);
        rf.Cells[0].AddParagraph("Il Direttore di cantiere");
        rf.Cells[0].AddParagraph(direttore ?? "").Format.Font.Bold = true;
        rf.Cells[1].AddParagraph(azienda.Compilati ? $"Per {azienda.RagioneSociale}" : "Per l'impresa");
        var linea = firme.AddRow();
        linea.TopPadding = Unit.FromPoint(20);
        foreach (var cella in new[] { linea.Cells[0], linea.Cells[1] })
            cella.AddParagraph("______________________________").Format.Font.Color = Colore(Stile.TestoSecondario);
    }

    private static void Piede(Section sec, string testo)
    {
        var piede = sec.Footers.Primary.AddParagraph();
        piede.Format.Font.Size = 7.5;
        piede.Format.Font.Color = Colore(Stile.TestoSecondario);
        piede.AddText(testo + " – pagina ");
        piede.AddPageField();
        piede.AddText(" di ");
        piede.AddNumPagesField();
    }

    private static Section NuovaSezione(Document doc)
    {
        var prima = doc.Sections[0];
        var sec = doc.AddSection();
        sec.PageSetup = prima.PageSetup.Clone();
        return sec;
    }
}
