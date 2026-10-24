using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;
using UglyToad.PdfPig;

namespace Corico.Modules.Preventivo.Tests;

public class QualificheTests
{
    private sealed record Contesto(AmbienteDiProva Amb, Guid CommessaId, Guid FornitoreId, Guid DirettoreCantiereId) : IAsyncDisposable
    {
        public T Get<T>() where T : notnull => Amb.Get<T>();
        public ValueTask DisposeAsync() => Amb.DisposeAsync();
    }

    /// <summary>Commessa con DL, RUP, CSE e Direttore di cantiere negli Attori, e un subappaltatore in anagrafica.</summary>
    private static async Task<Contesto> PreparaAsync()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Riqualificazione scuola media")).Id;
        var fornitore = await amb.Get<FornitoriService>().SalvaAsync(new DatiFornitore
        {
            Denominazione = "Edilzeta S.r.l.", PartitaIva = "01234567890", CodiceFiscale = "01234567890", Indirizzo = "Via Roma 1", Comune = "Catania", Provincia = "CT",
        });
        var attori = amb.Get<AttoriService>();
        async Task<Guid> Figura(RuoloCommessa ruolo, string nome, string? titolo, string? pec)
        {
            var a = await attori.SalvaAsync(new DatiAttore { Nominativo = nome, Titolo = titolo, Ente = "Comune di Caltagirone", Pec = pec });
            await attori.AggiungiIncaricoAsync(id, ruolo, a.Id, null);
            return a.Id;
        }
        await Figura(RuoloCommessa.DirettoreLavori, "Mario Bianchi", "Ing.", "dl@pec.it");
        await Figura(RuoloCommessa.Rup, "Anna Neri", "Arch.", "rup@pec.it");
        await Figura(RuoloCommessa.Cse, "Luca Verdi", "Geom.", null);
        var direttore = await Figura(RuoloCommessa.DirettoreCantiere, "Paolo Rossi", "Ing.", null);
        return new Contesto(amb, id, fornitore.Id, direttore);
    }

    private static async Task<(Guid Qualifica, Guid Bozza)> ApprovataAsync(Contesto c, TipoOperatore tipo = TipoOperatore.Subappaltatore)
    {
        var s = c.Get<QualificheService>();
        var qid = await s.CreaAsync(c.CommessaId, c.FornitoreId, tipo, null);
        var q = (await s.OttieniAsync(c.CommessaId, qid))!;
        q.OggettoPrestazioni = "Impermeabilizzazione delle coperture";
        foreach (var d in q.Documenti) d.Verificato = true;
        q.Documenti[1].Scadenza = new DateOnly(2027, 2, 15);
        await s.SalvaAsync(c.CommessaId, q);
        var bozza = await s.ApprovaAsync(c.CommessaId, qid, new DateOnly(2026, 10, 20), c.DirettoreCantiereId, null);
        return (qid, bozza);
    }

    [Fact]
    public async Task Approvazione_del_direttore_di_cantiere_genera_la_bozza_con_DL_RUP_e_CSE()
    {
        await using var c = await PreparaAsync();
        var s = c.Get<QualificheService>();
        var qid = await s.CreaAsync(c.CommessaId, c.FornitoreId, TipoOperatore.Subappaltatore, null);
        var q = (await s.OttieniAsync(c.CommessaId, qid))!;
        Assert.Equal("Edilzeta S.r.l.", q.Fornitore);
        Assert.Equal(StatoAutorizzazione.DaRichiedere, q.Autorizzazione);
        Assert.Contains(q.Documenti, d => d.Descrizione.Contains("DURC"));
        Assert.Contains(q.Documenti, d => d.Descrizione.Contains("POS"));

        // Senza documenti verificati non si approva.
        Assert.NotEmpty(QualificheService.ImpedimentiApprovazione(q));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.ApprovaAsync(c.CommessaId, qid, new DateOnly(2026, 10, 20), c.DirettoreCantiereId, null));
        Assert.Empty(await s.ComunicazioniAsync(c.CommessaId, qid));

        var (_, bozzaId) = await ApprovataAsync(c);
        q = (await s.OttieniAsync(c.CommessaId, qid))!;
        // La prima qualificazione è rimasta in istruttoria; quella approvata è la seconda.
        Assert.Equal(StatoQualifica.InIstruttoria, q.Stato);
        var bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        var approvata = (await s.OttieniAsync(c.CommessaId, bozza.QualificaId))!;
        Assert.Equal(StatoQualifica.Approvata, approvata.Stato);
        Assert.Equal("Ing. Paolo Rossi", approvata.DirettoreCantiere);
        Assert.Equal(new DateOnly(2026, 10, 20), approvata.DataConclusione);

        Assert.Equal(StatoComunicazione.Bozza, bozza.Stato);
        Assert.Equal("", bozza.Numero);
        Assert.Equal([RuoloCommessa.DirettoreLavori, RuoloCommessa.Rup, RuoloCommessa.Cse], bozza.Destinatari.Select(d => d.Ruolo));
        Assert.All(bozza.Destinatari, d => Assert.True(d.Selezionato));
        Assert.Equal("Mario Bianchi", bozza.Destinatari[0].Nominativo);
        Assert.Equal("dl@pec.it", bozza.Destinatari[0].Pec);
        Assert.Empty(bozza.Allegati);   // nessun documento del fornitore allegato in automatico

        var verifica = await s.VerificaAsync(c.CommessaId, bozzaId);
        Assert.True(verifica.Generabile);
        Assert.Contains(verifica.Avvisi, a => a.Contains("Luca Verdi"));   // CSE senza PEC né email
    }

    [Fact]
    public async Task Per_un_fornitore_il_CSE_non_e_selezionato_e_i_dati_mancanti_si_completano_a_mano()
    {
        await using var c = await PreparaAsync();
        var s = c.Get<QualificheService>();
        var (_, bozzaId) = await ApprovataAsync(c, TipoOperatore.Fornitore);
        var bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        Assert.False(bozza.Destinatari.Single(d => d.Ruolo == RuoloCommessa.Cse).Selezionato);

        bozza.Destinatari[0].Nominativo = null;
        await s.SalvaBozzaAsync(c.CommessaId, bozza);
        var verifica = await s.VerificaAsync(c.CommessaId, bozzaId);
        Assert.False(verifica.Generabile);
        Assert.Contains(verifica.Impedimenti, i => i.Contains("nominativo"));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.GeneraDefinitivoAsync(c.CommessaId, bozzaId));

        bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        bozza.Destinatari[0].Nominativo = "Mario Bianchi";
        await s.SalvaBozzaAsync(c.CommessaId, bozza);
        Assert.True((await s.VerificaAsync(c.CommessaId, bozzaId)).Generabile);
    }

    [Fact]
    public async Task Senza_attori_si_scrive_il_direttore_di_cantiere_e_i_destinatari_vuoti_vanno_completati()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        await using var _ = amb;
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa senza attori")).Id;
        var f = await amb.Get<FornitoriService>().SalvaAsync(new DatiFornitore { Denominazione = "Fimas S.r.l." });
        var s = amb.Get<QualificheService>();
        var qid = await s.CreaAsync(id, f.Id, TipoOperatore.Fornitore, null);
        var q = (await s.OttieniAsync(id, qid))!;
        foreach (var d in q.Documenti) d.Verificato = true;
        await s.SalvaAsync(id, q);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.ApprovaAsync(id, qid, new DateOnly(2026, 10, 20), null, " "));
        var bozzaId = await s.ApprovaAsync(id, qid, new DateOnly(2026, 10, 20), null, "Geom. Carlo Bruno");
        Assert.Equal("Geom. Carlo Bruno", (await s.OttieniAsync(id, qid))!.DirettoreCantiere);
        var bozza = (await s.ComunicazioneAsync(id, bozzaId))!;
        Assert.Equal(3, bozza.Destinatari.Count);
        Assert.All(bozza.Destinatari, d => Assert.Null(d.Nominativo));
        Assert.False((await s.VerificaAsync(id, bozzaId)).Generabile);
    }

    [Fact]
    public async Task Pdf_definitivo_numerato_archiviato_e_immutabile_poi_approvazione_invio_e_ricevuta()
    {
        await using var c = await PreparaAsync();
        var s = c.Get<QualificheService>();
        var (qid, bozzaId) = await ApprovataAsync(c);

        // Dopo l'esito la qualificazione non si modifica, salvo l'autorizzazione collegata.
        var q = (await s.OttieniAsync(c.CommessaId, qid))!;
        q.Prescrizioni = "Modifica non ammessa";
        q.Autorizzazione = StatoAutorizzazione.Richiesta;
        q.RiferimentoAutorizzazione = "Istanza prot. 123";
        await s.SalvaAsync(c.CommessaId, q);
        q = (await s.OttieniAsync(c.CommessaId, qid))!;
        Assert.Null(q.Prescrizioni);
        Assert.Equal(StatoAutorizzazione.Richiesta, q.Autorizzazione);

        var bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        bozza.Prescrizioni = "Accesso al cantiere solo con il preposto designato.";
        bozza.Note = "Il fascicolo è consultabile presso la sede.";
        await s.SalvaBozzaAsync(c.CommessaId, bozza);

        var anteprima = await c.Get<EsportazioniService>().ComunicazioneQualificaAsync(c.CommessaId, bozzaId);
        Assert.StartsWith("Bozza", anteprima!.Nome);

        var com = await s.GeneraDefinitivoAsync(c.CommessaId, bozzaId);
        Assert.Equal("CQ 2026/001", com.Numero);
        Assert.Equal(StatoComunicazione.PdfDefinitivo, com.Stato);
        Assert.NotNull(com.GenerataIl);
        Assert.False(string.IsNullOrEmpty(com.GenerataDa));
        Assert.Null(com.InviataIl);   // la generazione non vale come invio

        var documento = (await c.Get<DocumentiService>().ElencoAsync(c.CommessaId)).Single(d => d.Id == com.DocumentoId);
        Assert.Equal(QualificheService.TipoDocumentoComunicazione, documento.Tipo);
        Assert.Contains("CQ 2026-001", documento.NomeOriginale);

        var pdf = await c.Get<EsportazioniService>().ComunicazioneQualificaAsync(c.CommessaId, bozzaId);
        Assert.Equal(documento.NomeOriginale, pdf!.Nome);
        using (var lettura = PdfDocument.Open(pdf.Contenuto))
        {
            var testo = SenzaSpazi(string.Concat(lettura.GetPages().Select(p => p.Text)));
            Assert.Equal(3, lettura.NumberOfPages);   // lettera, attestato, elenco documenti
            Assert.Contains(SenzaSpazi("Edilzeta S.r.l."), testo);
            Assert.Contains("01234567890", testo);
            Assert.Contains(SenzaSpazi("Responsabile Unico del Progetto"), testo);
            Assert.Contains(SenzaSpazi("Mario Bianchi"), testo);
            Assert.Contains(SenzaSpazi("Paolo Rossi"), testo);
            Assert.Contains("POSITIVO", testo);
            Assert.Contains(SenzaSpazi("preposto designato"), testo);
            Assert.Contains(SenzaSpazi("non sostituisce"), testo);
            Assert.Contains(SenzaSpazi("ATTESTATO DI QUALIFICAZIONE"), testo);
            Assert.DoesNotContain("Prefettura", testo);
        }

        bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        bozza.Note = "altro";
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.SalvaBozzaAsync(c.CommessaId, bozza));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
            s.RegistraInvioAsync(c.CommessaId, bozzaId, new DateOnly(2026, 10, 21), ModalitaInvio.Pec, null, null));

        var operatore = new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);
        using (UtenteCorrente.Imposta(operatore))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.ApprovaInvioAsync(c.CommessaId, bozzaId));
        using (UtenteCorrente.Imposta(operatore with { PuoEmettereOrdini = true }))
            await s.ApprovaInvioAsync(c.CommessaId, bozzaId);

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
            s.RegistraInvioAsync(c.CommessaId, bozzaId, new DateOnly(2026, 10, 1), ModalitaInvio.Pec, null, null));
        await s.RegistraInvioAsync(c.CommessaId, bozzaId, new DateOnly(2026, 10, 22), ModalitaInvio.Pec, null, "Prot. 456");
        com = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        Assert.Equal(StatoComunicazione.Inviata, com.Stato);
        Assert.Equal("DL, RUP, CSE", com.DestinatariEffettivi);
        Assert.Equal("Mario Rossi", com.ApprovataDa);

        await s.RegistraRicevutaAsync(c.CommessaId, bozzaId, new DateOnly(2026, 10, 22), "Ricevuta di consegna PEC", null);
        com = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        Assert.Equal(StatoComunicazione.RicevutaAcquisita, com.Stato);

        // Fascicolo del fornitore: la qualificazione con la sua comunicazione.
        var fascicolo = await s.FascicoloAsync(c.FornitoreId);
        Assert.Single(fascicolo);
        Assert.Equal("CQ 2026/001", fascicolo[0].Comunicazioni.Single().Numero);

        // Riapertura bloccata finché la comunicazione definitiva è valida; dopo l'annullamento se ne prepara una nuova.
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.RiapriAsync(c.CommessaId, qid));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.NuovaBozzaAsync(c.CommessaId, qid));
        await s.AnnullaAsync(c.CommessaId, bozzaId, "Destinatario errato");
        var nuova = await s.NuovaBozzaAsync(c.CommessaId, qid);
        Assert.NotEqual(bozzaId, nuova);
    }

    [Fact]
    public async Task Allegati_solo_documenti_pertinenti_e_mai_quelli_riservati()
    {
        await using var c = await PreparaAsync();
        var s = c.Get<QualificheService>();
        var documenti = c.Get<DocumentiService>();
        var durc = await documenti.CaricaAsync(new MemoryStream("durc"u8.ToArray()), "durc.pdf", "application/pdf", c.CommessaId, QualificheService.TipoDocumentoQualifica);
        var visura = await documenti.CaricaAsync(new MemoryStream("visura"u8.ToArray()), "visura.pdf", "application/pdf", c.CommessaId, QualificheService.TipoDocumentoQualifica);
        var altra = (await c.Get<CommesseService>().CreaAsync("Altra commessa")).Id;
        var estraneo = await documenti.CaricaAsync(new MemoryStream("altro"u8.ToArray()), "altro.pdf", "application/pdf", altra);

        var qid = await s.CreaAsync(c.CommessaId, c.FornitoreId, TipoOperatore.Subappaltatore, null);
        var q = (await s.OttieniAsync(c.CommessaId, qid))!;
        foreach (var d in q.Documenti) d.Verificato = true;
        q.Documenti[0].DocumentoId = visura.Documento.Id;
        q.Documenti[1].DocumentoId = durc.Documento.Id;
        q.Documenti[1].Riservato = true;
        await s.SalvaAsync(c.CommessaId, q);
        var bozzaId = await s.ApprovaAsync(c.CommessaId, qid, new DateOnly(2026, 10, 20), c.DirettoreCantiereId, null);

        var bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        bozza.Allegati.Add(new AllegatoComunicazione { DocumentoId = durc.Documento.Id, Descrizione = "DURC" });
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.SalvaBozzaAsync(c.CommessaId, bozza));

        bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        bozza.Allegati.Add(new AllegatoComunicazione { DocumentoId = estraneo.Documento.Id, Descrizione = "Altro" });
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => s.SalvaBozzaAsync(c.CommessaId, bozza));

        bozza = (await s.ComunicazioneAsync(c.CommessaId, bozzaId))!;
        bozza.Allegati.Add(new AllegatoComunicazione { DocumentoId = visura.Documento.Id, Descrizione = "Visura camerale" });
        bozza.AllegaAttestato = false;
        await s.SalvaBozzaAsync(c.CommessaId, bozza);
        var com = await s.GeneraDefinitivoAsync(c.CommessaId, bozzaId);
        Assert.Equal("Visura camerale", com.Allegati.Single().Descrizione);

        var pdf = await c.Get<EsportazioniService>().ComunicazioneQualificaAsync(c.CommessaId, bozzaId);
        using var lettura = PdfDocument.Open(pdf!.Contenuto);
        Assert.Equal(2, lettura.NumberOfPages);   // lettera ed elenco documenti, senza attestato
        Assert.Contains("Visuracamerale", SenzaSpazi(lettura.GetPage(1).Text));
    }

    [Fact]
    public async Task Consultazione_non_modifica_le_qualificazioni()
    {
        await using var c = await PreparaAsync();
        var lettore = new IdentitaUtente(Guid.NewGuid(), "anna", "Anna Verdi", RuoloUtente.Consultazione);
        using (UtenteCorrente.Imposta(lettore))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
                c.Get<QualificheService>().CreaAsync(c.CommessaId, c.FornitoreId, TipoOperatore.Fornitore, null));
    }

    private static string SenzaSpazi(string s) => string.Concat(s.Where(ch => !char.IsWhiteSpace(ch)));

    [Theory]
    [InlineData(TipoAffidamento.Subappalto, TipoOperatore.Subappaltatore)]
    [InlineData(TipoAffidamento.Fornitura, TipoOperatore.Fornitore)]
    [InlineData(TipoAffidamento.NoloACaldo, TipoOperatore.SubAffidatario)]
    public void Tipologia_proposta_dal_tipo_di_affidamento(TipoAffidamento affidamento, TipoOperatore atteso) =>
        Assert.Equal(atteso, QualificheService.TipoDa(affidamento));
}
