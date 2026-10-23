using System.Text;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Core.Utenti;
using Corico.Modules.Documenti.Dominio;
using Corico.Modules.Documenti.Estrazione;
using Corico.Modules.Documenti.Servizi;

namespace Corico.Modules.Documenti.Tests;

public class LetturaERicercaTests
{
    private static Task<AmbienteDiProva> AmbienteAsync() => AmbienteDiProva.CreaAsync(new DocumentiModulo());

    private static async Task<Guid> CaricaAsync(AmbienteDiProva amb, byte[] contenuto, string nome, Guid? commessaId = null, string? tipo = null)
    {
        using var ms = new MemoryStream(contenuto);
        return (await amb.Get<DocumentiService>().CaricaAsync(ms, nome, null, commessaId, tipo)).Documento.Id;
    }

    [Fact]
    public async Task Pdf_con_testo_cercabile_per_pagina_senza_maiuscole_accenti_e_a_capo()
    {
        await using var amb = await AmbienteAsync();
        var id = await CaricaAsync(amb, DocumentiDiProva.PdfConTesto(
            ["Preventivo cliente n. 260561 del 12/02/2026", "Spett.le Impresa"],
            ["N.20 Deposito Rack", "PORTA REI60 7035 STANDARD", "Qualita certificata"]), "offerta.pdf");

        Assert.Equal(1, await amb.Get<LetturaArchivioService>().LeggiInCodaAsync());
        var ricerca = amb.Get<RicercaDocumentiService>();

        var trovati = await ricerca.CercaAsync(new FiltroRicerca("porta rei60"));
        var d = Assert.Single(trovati);
        Assert.Equal(id, d.Documento.Id);
        Assert.Equal(2, Assert.Single(d.Pagine).Numero);
        Assert.Contains(IndiceRicerca.InizioEvidenza, d.Pagine[0].Estratto);

        Assert.Single(await ricerca.CercaAsync(new FiltroRicerca("QUALITÀ")));       // accenti e maiuscole
        Assert.Single(await ricerca.CercaAsync(new FiltroRicerca("rack porta")));    // parole su righe diverse
        Assert.Single(await ricerca.CercaAsync(new FiltroRicerca("REI")));           // inizio di parola
        Assert.Empty(await ricerca.CercaAsync(new FiltroRicerca("finestra")));

        var testo = (await ricerca.TestoAsync(id))!;
        Assert.Equal(EsitoLettura.Completata, testo.Esito);
        Assert.Equal(MetodoLettura.Testo, testo.Metodo);
        Assert.Equal(Classificatore.Offerta, testo.TipoProposto);
    }

    [Theory]
    [InlineData("\"porta")]
    [InlineData("porta OR NOT")]
    [InlineData("rei* (porta) -x ^y")]
    [InlineData("   ")]
    public async Task Caratteri_speciali_nella_ricerca_non_danno_errori(string testo)
    {
        await using var amb = await AmbienteAsync();
        await CaricaAsync(amb, DocumentiDiProva.PdfConTesto(["Porta REI60 or not"]), "a.pdf");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca(testo));
    }

    [Fact]
    public async Task Scansione_letta_con_l_ocr_oppure_segnalata_se_l_ocr_manca()
    {
        await using var amb = await AmbienteAsync();
        var id = await CaricaAsync(amb, DocumentiDiProva.PdfScansione("COMPUTO METRICO ESTIMATIVO", "Porta tagliafuoco REI 60"), "perizia.pdf");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        var testo = (await amb.Get<RicercaDocumentiService>().TestoAsync(id))!;

        if (!amb.Get<LetturaArchivioService>().OcrDisponibile)
        {
            Assert.Equal(EsitoLettura.Parziale, testo.Esito);
            Assert.Contains("OCR", testo.Messaggio);
            return;
        }
        Assert.Equal(EsitoLettura.Completata, testo.Esito);
        Assert.Equal(MetodoLettura.Ocr, testo.Metodo);
        Assert.Equal(1, testo.PagineOcr);
        Assert.Equal(Classificatore.Computo, testo.TipoProposto);
        var trovato = Assert.Single(await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca("tagliafuoco")));
        Assert.True(trovato.Pagine[0].DaOcr);
    }

    [Fact]
    public async Task Pdf_misto_legge_con_l_ocr_solo_le_pagine_scansionate()
    {
        await using var amb = await AmbienteAsync();
        if (!amb.Get<LetturaArchivioService>().OcrDisponibile) return;
        // Pagina 1 con testo; la pagina 2 è un'immagine.
        var testoPdf = UglyToad.PdfPig.PdfDocument.Open(DocumentiDiProva.PdfConTesto(["Relazione tecnica generale del progetto"]));
        testoPdf.Dispose();
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Relazione tecnica generale del progetto", 11, new UglyToad.PdfPig.Core.PdfPoint(40, 780), font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddPng(DocumentiDiProva.Immagine("Ventilconvettore a parete"), new UglyToad.PdfPig.Core.PdfRectangle(0, 0, 595, 842));
        var id = await CaricaAsync(amb, builder.Build(), "misto.pdf");

        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        var testo = (await amb.Get<RicercaDocumentiService>().TestoAsync(id))!;
        Assert.Equal(MetodoLettura.Misto, testo.Metodo);
        Assert.Equal([false, true], testo.Pagine.Select(p => p.DaOcr));
        Assert.Equal(2, Assert.Single(await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca("ventilconvettore"))).Pagine[0].Numero);
    }

    [Fact]
    public async Task Word_paragrafi_e_tabelle()
    {
        await using var amb = await AmbienteAsync();
        await CaricaAsync(amb, DocumentiDiProva.Word(["Contratto di subappalto", "Opere di impermeabilizzazione"],
            [["Voce", "Importo"], ["Guaina bituminosa", "12.500,00"]]), "contratto.docx");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        var d = Assert.Single(await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca("guaina bituminosa")));
        Assert.Equal(Classificatore.Contratto, (await amb.Get<RicercaDocumentiService>().TestoAsync(d.Documento.Id))!.TipoProposto);
    }

    [Fact]
    public async Task Mail_con_allegato_l_allegato_entra_in_archivio_nella_stessa_commessa_e_si_cerca()
    {
        await using var amb = await AmbienteAsync();
        var commessa = (await amb.Get<CommesseService>().CreaAsync("Casa di Comunità")).Id;
        var offerta = DocumentiDiProva.PdfConTesto(["OFFERTA ECONOMICA", "Unita testaletto Aura 100"]);
        var mailId = await CaricaAsync(amb, DocumentiDiProva.Mail("fornitore@example.com", "Vs richiesta testaletti",
            "Buongiorno, in allegato la nostra proposta. Cordiali saluti", ("PR_3000 r00.pdf", offerta)), "richiesta.eml", commessa);

        Assert.Equal(2, await amb.Get<LetturaArchivioService>().LeggiInCodaAsync());
        var ricerca = amb.Get<RicercaDocumentiService>();

        var mail = (await ricerca.TestoAsync(mailId))!;
        Assert.Equal("Vs richiesta testaletti", mail.MailOggetto);
        Assert.Contains("fornitore@example.com", mail.MailMittente);
        Assert.Equal(Classificatore.Corrispondenza, mail.TipoProposto);

        var allegato = Assert.Single(await ricerca.AllegatiAsync(mailId));
        var trovato = Assert.Single(await ricerca.CercaAsync(new FiltroRicerca("testaletto", CommessaId: commessa)));
        Assert.Equal(allegato.AllegatoDocumentoId, trovato.Documento.Id);
        Assert.Equal(commessa, trovato.Documento.CommessaId);
        Assert.Equal(Classificatore.Offerta, (await ricerca.TestoAsync(trovato.Documento.Id))!.TipoProposto);
        // Il testo della mail è cercabile, con oggetto e nomi degli allegati.
        Assert.Single(await ricerca.CercaAsync(new FiltroRicerca("cordiali saluti")));
        Assert.Single(await ricerca.CercaAsync(new FiltroRicerca("PR_3000")));
    }

    [Fact]
    public async Task Filtri_per_commessa_tipo_e_data()
    {
        await using var amb = await AmbienteAsync();
        var a = (await amb.Get<CommesseService>().CreaAsync("Commessa A")).Id;
        var b = (await amb.Get<CommesseService>().CreaAsync("Commessa B")).Id;
        await CaricaAsync(amb, Encoding.UTF8.GetBytes("ponteggio a telaio commessa A"), "a.txt", a, "Offerta fornitore");
        await CaricaAsync(amb, Encoding.UTF8.GetBytes("ponteggio multidirezionale commessa B"), "b.txt", b, "Listino");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        var r = amb.Get<RicercaDocumentiService>();

        Assert.Equal(2, (await r.CercaAsync(new FiltroRicerca("ponteggio"))).Count);
        Assert.Equal("a.txt", Assert.Single(await r.CercaAsync(new FiltroRicerca("ponteggio", CommessaId: a))).Documento.NomeOriginale);
        Assert.Equal("b.txt", Assert.Single(await r.CercaAsync(new FiltroRicerca("ponteggio", Tipo: "listino"))).Documento.NomeOriginale);
        // Caricati il 21/10/2026 (orologio di prova).
        Assert.Empty(await r.CercaAsync(new FiltroRicerca("ponteggio", Dal: new DateOnly(2026, 10, 22))));
        Assert.Equal(2, (await r.CercaAsync(new FiltroRicerca("ponteggio", Al: new DateOnly(2026, 10, 21)))).Count);
    }

    [Fact]
    public async Task Tipo_proposto_cambia_il_documento_solo_dopo_la_conferma()
    {
        await using var amb = await AmbienteAsync();
        var id = await CaricaAsync(amb, Encoding.UTF8.GetBytes("STATO FINALE DEI LAVORI\nImporto del progetto originario 2.552.863,63"), "sf.txt");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        var r = amb.Get<RicercaDocumentiService>();
        var documenti = amb.Get<DocumentiService>();

        var proposta = Assert.Single(await r.ProposteTipoAsync());
        Assert.Equal(Classificatore.Sal, proposta.Tipo);
        Assert.Null(Assert.Single(await documenti.ElencoAsync()).Tipo);

        // La consultazione vede la proposta ma non la conferma.
        using (UtenteCorrente.Imposta(new IdentitaUtente(Guid.NewGuid(), "lettore", "Lettore", RuoloUtente.Consultazione)))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => r.EsaminaTipoAsync(id, conferma: true));
        Assert.Null(Assert.Single(await documenti.ElencoAsync()).Tipo);

        await r.EsaminaTipoAsync(id, conferma: true);
        Assert.Equal(Classificatore.Sal, Assert.Single(await documenti.ElencoAsync()).Tipo);
        Assert.Empty(await r.ProposteTipoAsync());
    }

    [Fact]
    public async Task Proposta_scartata_non_cambia_il_tipo_e_non_ricompare()
    {
        await using var amb = await AmbienteAsync();
        var id = await CaricaAsync(amb, Encoding.UTF8.GetBytes("Listino prezzi 2026 serramenti"), "l.txt", tipo: "Altro");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();
        var r = amb.Get<RicercaDocumentiService>();
        await r.EsaminaTipoAsync(id, conferma: false);
        Assert.Equal("Altro", Assert.Single(await amb.Get<DocumentiService>().ElencoAsync()).Tipo);
        Assert.Empty(await r.ProposteTipoAsync());
    }

    [Fact]
    public async Task Duplicati_simili_segnalati_anche_con_file_diversi()
    {
        await using var amb = await AmbienteAsync();
        var originale = DocumentiDiProva.TestoOfferta("Padilla", 1);
        await CaricaAsync(amb, Encoding.UTF8.GetBytes(originale), "offerta r00.txt");
        // Stessa offerta reinviata: una riga cambiata e impaginazione diversa.
        await CaricaAsync(amb, Encoding.UTF8.GetBytes(originale.Replace("riga 7 ", "riga 7 modificata ").Replace("\n", "\r\n\r\n")), "offerta r01.txt");
        await CaricaAsync(amb, Encoding.UTF8.GetBytes(DocumentiDiProva.TestoOfferta("Altro", 2)), "altra.txt");
        await amb.Get<LetturaArchivioService>().LeggiInCodaAsync();

        var coppia = Assert.Single(await amb.Get<RicercaDocumentiService>().DuplicatiSimiliAsync());
        Assert.Equal(["offerta r00.txt", "offerta r01.txt"], new[] { coppia.A.NomeOriginale, coppia.B.NomeOriginale }.Order());
        Assert.True(coppia.Somiglianza >= Somiglianza.Soglia);
    }

    [Fact]
    public async Task Formato_non_leggibile_resta_in_archivio_e_si_puo_rileggere()
    {
        await using var amb = await AmbienteAsync();
        var id = await CaricaAsync(amb, [1, 2, 3, 4], "disegno.dwg");
        var lettura = amb.Get<LetturaArchivioService>();
        await lettura.LeggiInCodaAsync();
        var r = amb.Get<RicercaDocumentiService>();
        Assert.Equal(EsitoLettura.NonSupportata, (await r.TestoAsync(id))!.Esito);
        Assert.Empty(await lettura.InCodaAsync());

        await lettura.RileggiAsync(id);
        Assert.Single(await lettura.InCodaAsync());
        await lettura.LeggiInCodaAsync();
        Assert.NotNull(await r.TestoAsync(id));
    }

    [Fact]
    public async Task Pdf_danneggiato_registrato_come_errore_senza_bloccare_la_coda()
    {
        await using var amb = await AmbienteAsync();
        var rotto = await CaricaAsync(amb, Encoding.ASCII.GetBytes("%PDF-1.4 non è un pdf"), "rotto.pdf");
        await CaricaAsync(amb, Encoding.UTF8.GetBytes("ponteggio"), "buono.txt");
        Assert.Equal(2, await amb.Get<LetturaArchivioService>().LeggiInCodaAsync());
        var stato = await amb.Get<RicercaDocumentiService>().StatoAsync();
        Assert.Equal(2, stato.Letti);
        Assert.Equal(rotto, Assert.Single(stato.DaVerificare).Documento.Id);
        Assert.Single(await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca("ponteggio")));
    }

    [Fact]
    public async Task Indice_ricostruito_dal_testo_se_manca()
    {
        await using var amb = await AmbienteAsync();
        var id = await CaricaAsync(amb, Encoding.UTF8.GetBytes("impermeabilizzazione copertura"), "a.txt");
        var lettura = amb.Get<LetturaArchivioService>();
        await lettura.LeggiInCodaAsync();
        await amb.Get<IndiceRicerca>().RimuoviAsync(id, default);
        Assert.Empty(await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca("copertura")));

        await lettura.RicostruisciIndiceSeVuotoAsync();
        Assert.Single(await amb.Get<RicercaDocumentiService>().CercaAsync(new FiltroRicerca("copertura")));
    }
}
