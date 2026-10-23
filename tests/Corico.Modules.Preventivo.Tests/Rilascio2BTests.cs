using ClosedXML.Excel;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Esportazioni;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>Rilascio 2B: richieste d'offerta, confronto avanzato, aggiornamento dei prezzi di costo.</summary>
public class Rilascio2BTests
{
    /// <summary>Pacchetto "Porte": voci A (10 cad, Fornitura 400) e B (2 cad, Fornitura 500) con l'offerta Padilla n. 123 scelta.</summary>
    private static async Task<(AmbienteDiProva Amb, Guid CommessaId, Guid PacchettoId, Guid OffertaId)> PreparaAsync()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "A", Descrizione = "Porta a un'anta", Unita = "cad", Quantita = 10, Prezzo = 600 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "B", Descrizione = "Porta a due ante", Unita = "cad", Quantita = 2, Prezzo = 900 });
        var voci = await prv.VociAsync(id);

        var pacchetti = amb.Get<PacchettiService>();
        var pacchetto = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte", Tipo = TipoAffidamento.Fornitura });
        await pacchetti.AssegnaVociAsync(id, pacchetto, [voci[0].Id, voci[1].Id]);
        var offerta = await pacchetti.NuovaOffertaAsync(id, pacchetto);
        offerta.NomeFornitore = "Padilla";
        offerta.Numero = "123";
        offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id).Fornitura = 400;
        offerta.Voci.Single(v => v.VoceComputoId == voci[1].Id).Fornitura = 500;
        await pacchetti.SalvaOffertaAsync(id, offerta);
        var scelta = (await pacchetti.DettaglioAsync(id, pacchetto))!.Scelta!.Offerta.Id;
        return (amb, id, pacchetto, scelta);
    }

    /// <summary>Compila l'Excel della richiesta come farebbe il fornitore: prezzo per riga e condizioni.</summary>
    private static MemoryStream Compila(byte[] excel, Func<string, decimal?> prezzoPerCodice)
    {
        using var wb = new XLWorkbook(new MemoryStream(excel));
        var ws = wb.Worksheet(EsportazioneExcel.FoglioRichiesta);
        foreach (var riga in ws.RowsUsed())
        {
            if (!Guid.TryParse(riga.Cell(1).GetString(), out _)) continue;
            if (prezzoPerCodice(riga.Cell(3).GetString()) is { } p) riga.Cell(EsportazioneExcel.ColonnaPrezzoRichiesta).Value = p;
        }
        void Scrivi(string etichetta, string valore) =>
            ws.Column(2).CellsUsed().First(c => c.GetString() == etichetta).CellRight().Value = valore;
        Scrivi("Validità dell'offerta (giorni)", "30");
        Scrivi("Pagamento", "90 gg d.f.f.m.");
        Scrivi("Vostro numero d'offerta", "R-55");
        Scrivi("Esclusioni e oneri a vostro carico", "Scarico al piano; smaltimento imballi");
        var uscita = new MemoryStream();
        wb.SaveAs(uscita);
        uscita.Position = 0;
        return uscita;
    }

    [Fact]
    public async Task Richiesta_dal_pacchetto_senza_prezzi_e_offerta_registrata_dall_excel_compilato()
    {
        var (amb, id, pacchetto, _) = await PreparaAsync();
        await using var _a = amb;
        var richieste = amb.Get<RichiesteService>();

        var rid = await richieste.CreaDaPacchettoAsync(id, pacchetto);
        var r = (await richieste.OttieniAsync(id, rid))!;
        Assert.Equal("RdO 2026/001", r.Numero);
        Assert.Equal(new DateOnly(2026, 10, 31), r.Scadenza);
        Assert.Equal(["A", "B"], r.Righe.OrderBy(x => x.Ordine).Select(x => x.Codice));
        Assert.Equal(10m, r.Righe.Single(x => x.Codice == "A").Quantita);
        var padilla = Assert.Single(r.Destinatari);                 // proposto dalle offerte già presenti
        Assert.Equal("Padilla", padilla.Fornitore);
        Assert.Equal(StatoRisposta.DaInviare, padilla.Stato);

        r.Destinatari.Add(new DestinatarioRichiesta { Fornitore = "Rossi Serramenti" });
        await richieste.SalvaAsync(id, r);
        await richieste.SegnaInviataAsync(id, rid);
        r = (await richieste.OttieniAsync(id, rid))!;
        Assert.All(r.Destinatari, d => Assert.Equal(StatoRisposta.Inviata, d.Stato));

        var esportazioni = amb.Get<EsportazioniService>();
        var rossi = r.Destinatari.Single(d => d.Fornitore == "Rossi Serramenti");
        var pdf = (await esportazioni.RichiestaAsync(id, rid, rossi.Id, excel: false))!;
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Contenuto, 0, 4));
        Assert.Contains("Rossi Serramenti", pdf.Nome);
        var excel = (await esportazioni.RichiestaAsync(id, rid, rossi.Id, excel: true))!;

        // L'Excel non contiene prezzi finché il fornitore non lo compila.
        var vuoto = RichiesteService.LeggiExcelCompilato(new MemoryStream(excel.Contenuto));
        Assert.Empty(vuoto.Righe);

        var compilato = RichiesteService.LeggiExcelCompilato(Compila(excel.Contenuto, c => c == "A" ? 380m : c == "B" ? 480m : null));
        Assert.Equal(2, compilato.Righe.Count);
        Assert.Equal("R-55", compilato.Numero);
        var (offertaId, pid, prezzi) = await richieste.RegistraOffertaAsync(id, rossi.Id, null, compilato);
        Assert.Equal(pacchetto, pid);
        Assert.Equal(2, prezzi);

        var o = (await amb.Get<PacchettiService>().OffertaAsync(id, offertaId))!;
        Assert.Equal("Rossi Serramenti", o.NomeFornitore);
        Assert.Equal("R-55", o.Numero);
        Assert.Equal("90 gg d.f.f.m.", o.Pagamento);
        Assert.Equal(new DateOnly(2026, 11, 20), o.Scadenza);
        Assert.All(o.Righe, x => Assert.NotNull(x.VoceComputoId));

        r = (await richieste.OttieniAsync(id, rid))!;
        Assert.Equal(StatoRisposta.Ricevuta, r.Destinatari.Single(d => d.Id == rossi.Id).Stato);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => richieste.RegistraOffertaAsync(id, rossi.Id, null));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => richieste.EliminaAsync(id, rid));

        // Confronto: la nuova offerta costa 10 × 380 + 2 × 480 = 4.760 contro 5.000 di Padilla, più le esclusioni stimate.
        var confronto = amb.Get<ConfrontoService>();
        var c = (await confronto.ConfrontoAsync(id, pacchetto))!;
        var nuova = c.Offerte.Single(x => x.Offerta.Id == offertaId);
        Assert.Equal(4_760m, nuova.Risultato.Costo);
        Assert.Equal(nuova.Offerta.Id, c.Migliore!.Offerta.Id);
        Assert.Equal(["Scarico al piano", "smaltimento imballi"], ConfrontoService.EsclusioniDaTesto(nuova.Offerta.Esclusioni));
        await confronto.SalvaStimeAsync(id, offertaId, [
            new StimaEsclusione { Descrizione = "Scarico al piano", Importo = 200 },
            new StimaEsclusione { Descrizione = "Smaltimento imballi", Importo = 100 },
        ]);
        c = (await confronto.ConfrontoAsync(id, pacchetto))!;
        nuova = c.Offerte.Single(x => x.Offerta.Id == offertaId);
        Assert.Equal(5_060m, nuova.CostoConfrontabile);
        Assert.Equal("Padilla", c.Migliore!.Offerta.NomeFornitore);   // a pari condizioni Padilla costa meno
        var voceA = c.Voci.Single(v => v.Voce.Tariffa == "A");
        Assert.True(voceA.Celle[c.Offerte.ToList().FindIndex(x => x.Offerta.Id == offertaId)].Migliore);

        // Le esclusioni diventano costo solo per l'offerta scelta, una alla volta.
        var stima = nuova.Stime.First();
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => confronto.TrasformaInCostoAsync(id, stima.Id));
        var pacchetti = amb.Get<PacchettiService>();
        var dettaglio = (await pacchetti.DettaglioAsync(id, pacchetto))!;
        using (UtenteCorrente.Imposta(new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore)))
            await pacchetti.ScegliOffertaAsync(id, pacchetto, dettaglio.Pacchetto.Versione, offertaId);
        await confronto.TrasformaInCostoAsync(id, stima.Id);
        c = (await confronto.ConfrontoAsync(id, pacchetto))!;
        Assert.Equal("Mario Rossi", c.SceltaDa);
        nuova = c.Offerte.Single(x => x.Offerta.Id == offertaId);
        Assert.Equal(4_960m, nuova.Risultato.Costo);
        Assert.Equal(5_060m, nuova.CostoConfrontabile);                // il totale a pari condizioni non cambia
        Assert.Single(nuova.StimeDaTrasformare);
    }

    [Fact]
    public async Task Richiesta_dalla_lista_della_spesa_con_quantita_totale_e_numerazione_progressiva()
    {
        var (amb, id, pacchetto, _) = await PreparaAsync();
        await using var _a = amb;
        var richieste = amb.Get<RichiesteService>();
        await richieste.CreaDaPacchettoAsync(id, pacchetto);

        var lista = await amb.Get<ListaSpesaService>().CalcolaAsync(id);
        var chiavi = lista.Righe.Select(r => r.ChiaveOrdine).ToList();
        Assert.NotEmpty(chiavi);
        var rid = await richieste.CreaDaListaAsync(id, chiavi);
        var r = (await richieste.OttieniAsync(id, rid))!;
        Assert.Equal("RdO 2026/002", r.Numero);
        Assert.Equal(lista.Righe.Sum(x => x.Quantita), r.Righe.Sum(x => x.Quantita));
        Assert.All(r.Righe, x => Assert.Equal(pacchetto, x.PacchettoId));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => richieste.CreaDaListaAsync(id, []));

        // Senza offerte registrate la richiesta si elimina (solo l'amministratore).
        using (UtenteCorrente.Imposta(new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore)))
            await Assert.ThrowsAnyAsync<Exception>(() => richieste.EliminaAsync(id, rid));
        await richieste.EliminaAsync(id, rid);
        Assert.Single(await richieste.ElencoAsync(id));
    }

    [Fact]
    public async Task Variazione_percentuale_applicata_solo_alle_righe_scelte_e_annullabile()
    {
        var (amb, id, pacchetto, _) = await PreparaAsync();
        await using var _a = amb;
        var servizio = amb.Get<AggiornamentoPrezziService>();
        var variazioni = new Dictionary<CategoriaRisorsa, decimal> { [CategoriaRisorsa.Materiali] = 10 };

        var proposte = await servizio.ProposteAsync(id, variazioni);
        Assert.Equal(5_000m, proposte.CostoAttuale);
        Assert.Equal(2, proposte.Righe.Count);
        var a = proposte.Righe.Single(r => r.Tariffa == "A");
        Assert.Equal(440m, a.PrezzoNuovo);
        Assert.Equal(400m, a.DifferenzaCosto);
        Assert.Equal(proposte.Mdc - 400m, proposte.Dopo([a]).Mdc);

        var operatore = new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);
        using (UtenteCorrente.Imposta(operatore))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => servizio.ConfermaAsync(id, [a.Id], variazioni, null));
        AggiornamentoPrezzi agg;
        using (UtenteCorrente.Imposta(operatore with { PuoEmettereOrdini = true }))
            agg = await servizio.ConfermaAsync(id, [a.Id], variazioni, "Listino novembre");
        Assert.Equal(1, agg.NumeroRighe);
        Assert.Equal(400m, agg.DifferenzaCosto);
        Assert.Equal(proposte.Mdc - 400m, agg.MdcDopo);

        var scelta = (await amb.Get<PacchettiService>().DettaglioAsync(id, pacchetto))!.Scelta!;
        Assert.Equal(5_400m, scelta.Risultato.Costo);
        var storico = Assert.Single(await servizio.StoricoAsync(id));
        Assert.Equal("Mario Rossi", storico.CreatoDa);

        // Senza variazioni non resta nulla da proporre; con +10% la voce A parte dal nuovo prezzo.
        Assert.Empty((await servizio.ProposteAsync(id)).Righe);
        Assert.Equal(484m, (await servizio.ProposteAsync(id, variazioni)).Righe.Single(r => r.Tariffa == "A").PrezzoNuovo);

        await servizio.AnnullaAsync(id, agg.Id);
        Assert.Equal(5_000m, (await amb.Get<PacchettiService>().DettaglioAsync(id, pacchetto))!.Scelta!.Risultato.Costo);
        Assert.NotNull((await servizio.StoricoAsync(id))[0].AnnullatoIl);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => servizio.AnnullaAsync(id, agg.Id));
    }

    [Fact]
    public async Task Annullamento_bloccato_se_il_prezzo_e_stato_modificato_di_nuovo()
    {
        var (amb, id, pacchetto, offertaId) = await PreparaAsync();
        await using var _a = amb;
        var servizio = amb.Get<AggiornamentoPrezziService>();
        var variazioni = new Dictionary<CategoriaRisorsa, decimal> { [CategoriaRisorsa.Materiali] = -5 };
        var proposte = await servizio.ProposteAsync(id, variazioni);
        var agg = await servizio.ConfermaAsync(id, proposte.Righe.Select(r => r.Id).ToList(), variazioni, null);
        Assert.Equal(-250m, agg.DifferenzaCosto);

        var pacchetti = amb.Get<PacchettiService>();
        var o = (await pacchetti.OffertaAsync(id, offertaId))!;
        o.Voci.First(v => v.Fornitura == 380).Fornitura = 390;
        await pacchetti.SalvaOffertaAsync(id, o);

        var ex = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => servizio.AnnullaAsync(id, agg.Id));
        Assert.Contains("modificat", ex.Message);
        Assert.Null((await servizio.StoricoAsync(id))[0].AnnullatoIl);
    }

    [Fact]
    public async Task Nuova_revisione_dell_offerta_scelta_proposta_e_annullabile()
    {
        var (amb, id, pacchetto, offertaId) = await PreparaAsync();
        await using var _a = amb;
        var pacchetti = amb.Get<PacchettiService>();
        amb.Orologio.Advance(TimeSpan.FromHours(1));
        var rev = await pacchetti.NuovaOffertaAsync(id, pacchetto);
        rev.NomeFornitore = "Padilla";
        rev.Numero = "123";
        rev.Revisione = "rev. 1";
        foreach (var v in rev.Voci) v.Fornitura = 450;
        var revId = await pacchetti.SalvaOffertaAsync(id, rev);

        var servizio = amb.Get<AggiornamentoPrezziService>();
        var proposte = await servizio.ProposteAsync(id);
        var r = Assert.Single(proposte.Righe);
        Assert.Equal(FontePrezzo.Revisione, r.Fonte);
        Assert.Equal(5_000m, r.PrezzoAttuale);
        Assert.Equal(5_400m, r.PrezzoNuovo);                         // 12 × 450
        Assert.Equal(400m, r.DifferenzaCosto);

        await servizio.ConfermaAsync(id, [r.Id], null, null);
        var d = (await pacchetti.DettaglioAsync(id, pacchetto))!;
        Assert.Equal(revId, d.Pacchetto.OffertaSceltaId);
        Assert.Empty((await servizio.ProposteAsync(id)).Righe);

        var agg = (await servizio.StoricoAsync(id))[0];
        await servizio.AnnullaAsync(id, agg.Id);
        Assert.Equal(offertaId, (await pacchetti.DettaglioAsync(id, pacchetto))!.Pacchetto.OffertaSceltaId);
    }
}
