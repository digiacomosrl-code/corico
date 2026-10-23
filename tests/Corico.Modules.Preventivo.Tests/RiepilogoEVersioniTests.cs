using Corico.Core.Utenti;
using ClosedXML.Excel;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Tests;

public class RiepilogoEVersioniTests
{
    /// <summary>
    /// Commessa con ribasso 20%: voci A (10 × 600) e B (2 × 900) nel pacchetto "Porte" con costo 400 e 500 a voce,
    /// voce C (1 × 1.000) non assegnata.
    /// </summary>
    private static async Task<(AmbienteDiProva Amb, Guid CommessaId, Guid PacchettoId, Guid OffertaId)> PreparaAsync()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "A", Descrizione = "Porta a un'anta", Unita = "cad", Quantita = 10, Prezzo = 600 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "B", Descrizione = "Porta a due ante", Unita = "cad", Quantita = 2, Prezzo = 900 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "C", Descrizione = "Opere varie", Unita = "a corpo", Quantita = 1, Prezzo = 1000 });
        await prv.ImpostaRibassoAsync(id, (await prv.StatoAsync(id)).Versione, 20m);
        var voci = await prv.VociAsync(id);

        var pacchetti = amb.Get<PacchettiService>();
        var pacchetto = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte", Tipo = TipoAffidamento.Fornitura });
        await pacchetti.AssegnaVociAsync(id, pacchetto, [voci[0].Id, voci[1].Id]);
        var offerta = await pacchetti.NuovaOffertaAsync(id, pacchetto);
        offerta.NomeFornitore = "Padilla";
        offerta.Data = new DateOnly(2026, 9, 1);
        offerta.ValiditaGiorni = 30;
        // La nuova offerta propone già una riga di costo per ogni voce del pacchetto.
        offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id).Fornitura = 400;
        offerta.Voci.Single(v => v.VoceComputoId == voci[1].Id).Fornitura = 500;
        await pacchetti.SalvaOffertaAsync(id, offerta);
        var offertaId = (await pacchetti.DettaglioAsync(id, pacchetto))!.Scelta!.Offerta.Id;
        return (amb, id, pacchetto, offertaId);
    }

    [Fact]
    public async Task Riepilogo_con_integrazioni_spese_generali_e_risultato()
    {
        var (amb, id, pacchetto, _) = await PreparaAsync();
        await using var _ = amb;
        var r = amb.Get<RiepilogoService>();
        await r.SalvaIntegrazioneAsync(id, new IntegrazioneRicavo { Descrizione = "Oneri di discarica", Importo = 100, PacchettoId = pacchetto });
        await r.SalvaIntegrazioneAsync(id, new IntegrazioneRicavo { Descrizione = "Rimborso spese", Importo = 50 });
        await r.SalvaSpesaAsync(id, new SpesaGenerale { Descrizione = "Cantiere", Modo = ModoSpesaGenerale.Importo, Importo = 1000 });
        await r.SalvaSpesaAsync(id, new SpesaGenerale { Descrizione = "Sede", Modo = ModoSpesaGenerale.Percentuale, Percentuale = 5, Base = BaseSpesaGenerale.RicavoCommessa });

        var t = (await r.FotografiaAsync(id)).Totali;
        Assert.Equal(7_040m, t.RicavoNettoComputo);         // (6.000 + 1.800 + 1.000) × 0,8
        Assert.Equal(6_340m, t.RicavoPacchetti);            // 6.240 + integrazione di pacchetto 100
        Assert.Equal(800m, t.RicavoNonAssegnato);
        Assert.Equal(7_190m, t.Ricavo);                     // + integrazione di commessa 50
        Assert.Equal(5_000m, t.Costo);                      // 10 × 400 + 2 × 500
        Assert.Equal(2_190m, t.Mdc);
        Assert.Equal(1_359.50m, t.SpeseGenerali);           // 1.000 + 5% di 7.190
        Assert.Equal(830.50m, t.Risultato);
        Assert.Equal(800m, t.RicavoSenzaCosto);

        // Il tabellone dei pacchetti comprende l'integrazione di pacchetto.
        var riga = (await amb.Get<PacchettiService>().TabelloneAsync(id)).Righe.Single();
        Assert.Equal(6_340m, riga.Ricavo);
        Assert.Equal(1_340m, riga.Mdc);
    }

    [Fact]
    public async Task Avvisi_essenziali()
    {
        var (amb, id, _, _) = await PreparaAsync();
        await using var _ = amb;
        var r = amb.Get<RiepilogoService>();
        await r.ImpostaObiettivoAsync(id, 40m);

        var avvisi = await r.AvvisiAsync(id);
        Assert.Contains(avvisi, a => a.Gravita == GravitaAvviso.Attenzione && a.Problema.Contains("scaduta il 01/10/2026"));
        Assert.Contains(avvisi, a => a.Problema.Contains("1 voci del computo non sono assegnate"));
        Assert.Contains(avvisi, a => a.Problema.Contains("sotto l'obiettivo del 40,00 %"));
        Assert.DoesNotContain(avvisi, a => a.Gravita == GravitaAvviso.Errore);
    }

    [Fact]
    public async Task Versioni_immutabili_numerate_e_confrontabili()
    {
        var (amb, id, _, _) = await PreparaAsync();
        await using var _ = amb;
        var versioni = amb.Get<VersioniService>();
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => versioni.PubblicaAsync(id, "  "));
        Assert.Null(await versioni.ImpattoAsync(id));

        var v1 = await versioni.PubblicaAsync(id, "Offerta di gara");
        Assert.Equal(1, v1.Numero);
        Assert.Equal(7_040m, v1.Ricavo);
        Assert.Equal(5_000m, v1.Costo);
        Assert.True((await versioni.ImpattoAsync(id))!.NessunaDifferenza);

        // Una versione ufficiale non si può modificare né eliminare, nemmeno accedendo direttamente ai dati.
        await using (var db = await amb.Get<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync())
        {
            var salvata = await db.Versioni.SingleAsync();
            salvata.Nota = "Modificata";
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => db.SaveChangesAsync());
        }
        await using (var db = await amb.Get<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync())
        {
            db.Versioni.Remove(await db.Versioni.SingleAsync());
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => db.SaveChangesAsync());
        }

        // Modifica della bozza: prezzo di una voce e nuova voce.
        var prv = amb.Get<PreventivoService>();
        var a = (await prv.VociAsync(id)).Single(v => v.Tariffa == "A");
        await prv.SalvaVoceAsync(id, new DatiVoce { Id = a.Id, Versione = a.Versione, Tariffa = "A", Descrizione = a.Descrizione, Unita = "cad", Quantita = 10, Prezzo = 650 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "D", Descrizione = "Nuova voce", Unita = "cad", Quantita = 1, Prezzo = 100 });

        var impatto = (await versioni.ImpattoAsync(id))!;
        Assert.False(impatto.NessunaDifferenza);
        var ricavo = impatto.Totali.Single(x => x.Grandezza == "Ricavo di commessa");
        Assert.Equal(7_040m, ricavo.Prima);
        Assert.Equal(7_040m + 400m + 80m, ricavo.Dopo);     // +50 × 10 × 0,8 e nuova voce 100 × 0,8
        Assert.Contains(impatto.Voci, v => v.Tariffa == "A" && v.Esito == EsitoConfronto.Modificato && v.PrezzoPrima == 600 && v.PrezzoDopo == 650);
        Assert.Contains(impatto.Voci, v => v.Tariffa == "D" && v.Esito == EsitoConfronto.Aggiunto);
        Assert.Contains(impatto.Pacchetti, p => p.Descrizione == "Porte" && p.Esito == EsitoConfronto.Modificato);
        Assert.Contains(await amb.Get<RiepilogoService>().AvvisiAsync(id), x => x.Problema.Contains("cambiata rispetto alla versione ufficiale n. 1"));

        var v2 = await versioni.PubblicaAsync(id, "Revisione prezzi");
        Assert.Equal(2, v2.Numero);
        var confronto = await versioni.ConfrontaAsync(id, v1.Id, v2.Id);
        Assert.Equal(2, confronto.Voci.Count);
        Assert.Equal(1, (await amb.Get<StoricoService>().PerCommessaAsync(id)).Count(s => s.Operazione == "Pubblicazione" && s.Nota!.Contains("n. 1")));
    }

    [Fact]
    public async Task Esportazioni_excel_e_pdf()
    {
        var (amb, id, pacchetto, _) = await PreparaAsync();
        await using var _ = amb;
        var e = amb.Get<EsportazioniService>();

        var computo = (await e.ComputoExcelAsync(id, null))!;
        using (var wb = new XLWorkbook(new MemoryStream(computo.Contenuto)))
        {
            var ws = wb.Worksheet("Computo");
            var totale = ws.CellsUsed().Single(c => c.GetString() == "Totale").WorksheetRow();
            Assert.Equal(8_800m, (decimal)totale.Cell(7).Value.GetNumber());
            Assert.Equal(7_040m, (decimal)totale.Cell(9).Value.GetNumber());
        }

        var riepilogo = (await e.RiepilogoExcelAsync(id, null))!;
        using (var wb = new XLWorkbook(new MemoryStream(riepilogo.Contenuto)))
            Assert.True(wb.Worksheets.Contains("Pacchetti"));

        var pdf = (await e.RiepilogoPdfAsync(id, null))!;
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Contenuto, 0, 4));
        var scheda = (await e.PacchettoPdfAsync(id, pacchetto))!;
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(scheda.Contenuto, 0, 4));
        Assert.StartsWith("Pacchetto 1 Porte", scheda.Nome);

        var v = await amb.Get<VersioniService>().PubblicaAsync(id, "Prima versione");
        var pdfVersione = (await e.RiepilogoPdfAsync(id, v.Id))!;
        Assert.Contains("v1", pdfVersione.Nome);
        Assert.Null(await e.RiepilogoPdfAsync(id, Guid.NewGuid()));
    }

    [Fact]
    public async Task La_lista_della_spesa_segnala_le_voci_non_assegnate()
    {
        var (amb, id, _, _) = await PreparaAsync();
        await using var _ = amb;
        var lista = await amb.Get<ListaSpesaService>().CalcolaAsync(id);
        Assert.Equal(5_000m, lista.Totale);
        Assert.Equal(0, lista.PacchettiSenzaOfferta);
        Assert.Equal(1, lista.VociNonAssegnate);    // la voce C
    }

    [Fact]
    public async Task Le_risorse_della_lista_hanno_un_codice_stabile_e_la_descrizione_senza_il_gruppo()
    {
        var (amb, id, _, _) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<ListaSpesaService>();
        var lista = await servizio.CalcolaAsync(id);
        Assert.Equal(["MAT.001", "MAT.002"], lista.Righe.Select(r => r.Codice).ToArray());
        Assert.Equal("Porta a un'anta", lista.Righe[0].Descrizione);

        // Una nuova voce nel pacchetto riceve il codice successivo; quelle esistenti non cambiano.
        var prv = amb.Get<PreventivoService>();
        var pacchetti = amb.Get<PacchettiService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "D", Descrizione = "Maniglione antipanico", Unita = "cad", Quantita = 4, Prezzo = 300 });
        var d = (await prv.VociAsync(id)).Single(v => v.Tariffa == "D");
        var pacchetto = (await pacchetti.TabelloneAsync(id)).Righe.Single().Id;
        await pacchetti.AssegnaVociAsync(id, pacchetto, [d.Id]);
        var dettaglio = (await pacchetti.DettaglioAsync(id, pacchetto))!;
        var offerta = (await pacchetti.OffertaAsync(id, dettaglio.Scelta!.Offerta.Id))!;
        offerta.Voci.Single(v => v.VoceComputoId == d.Id).Fornitura = 200;
        await pacchetti.SalvaOffertaAsync(id, offerta);

        var dopo = (await servizio.CalcolaAsync(id)).Aggregata();
        Assert.Equal(["MAT.001", "MAT.002", "MAT.003"], dopo.Select(r => r.Codice).ToArray());
        Assert.Equal("Maniglione antipanico", dopo.Single(r => r.Codice == "MAT.003").Descrizione);
    }

    [Fact]
    public async Task Pubblicazione_riservata_all_amministratore_e_consultazione_in_lettura()
    {
        var (amb, id, _, _) = await PreparaAsync();
        await using var _ = amb;
        var operatore = new IdentitaUtente(Guid.NewGuid(), "operatore", "Oreste Operatore", RuoloUtente.Operatore);
        var lettore = new IdentitaUtente(Guid.NewGuid(), "lettore", "Lucia Lettrice", RuoloUtente.Consultazione);

        using (UtenteCorrente.Imposta(operatore))
        {
            var ex = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => amb.Get<VersioniService>().PubblicaAsync(id, "Offerta di gara"));
            Assert.Contains("amministratore", ex.Message);
        }

        using (UtenteCorrente.Imposta(lettore))
        {
            // Consultare preventivo, riepilogo e lista della spesa non è una modifica.
            Assert.NotEmpty(await amb.Get<PreventivoService>().VociAsync(id));
            await amb.Get<RiepilogoService>().AvvisiAsync(id);
            await amb.Get<ListaSpesaService>().CalcolaAsync(id);
            var voce = (await amb.Get<PreventivoService>().VociAsync(id)).First();
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => amb.Get<PreventivoService>().SalvaVoceAsync(id,
                new DatiVoce { Id = voce.Id, Versione = voce.Versione, Tariffa = voce.Tariffa, Descrizione = "Cambiata", Unita = voce.Unita, Quantita = 1, Prezzo = 1 }));
        }
    }
}
