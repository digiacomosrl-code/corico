using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class PacchettiServiceTests
{
    private static async Task<(AmbienteDiProva Amb, Guid CommessaId, Guid PacchettoId, IReadOnlyList<RigaVoce> Voci)> PreparaAsync()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "27.01.04.001", Descrizione = "Porta REI 60 a un'anta", Unita = "cad", Quantita = 10, Prezzo = 600 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "27.01.04.002", Descrizione = "Porta REI 60 a due ante", Unita = "cad", Quantita = 2, Prezzo = 900 });
        await prv.ImpostaRibassoAsync(id, (await prv.StatoAsync(id)).Versione, 20m);
        var voci = await prv.VociAsync(id);
        var pacchetto = await amb.Get<PacchettiService>().SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte REI", Tipo = TipoAffidamento.Fornitura });
        await amb.Get<PacchettiService>().AssegnaVociAsync(id, pacchetto, voci.Select(v => v.Id).ToList());
        return (amb, id, pacchetto, voci);
    }

    [Fact]
    public void Importo_netto_delle_righe_come_nell_offerta_Padilla()
    {
        // 2 porte a 307,00 € con sconto del 43%: importo netto 349,98 €.
        Assert.Equal(349.98m, CalcoloPacchetti.ImportoRiga(2, 307m, 43m));
        Assert.Equal(6.84m, CalcoloPacchetti.ImportoRiga(1, 12m, 43m));
    }

    [Fact]
    public async Task Piu_righe_d_offerta_sulla_stessa_voce_e_voci_a_corpo()
    {
        var (amb, id, pacchetto, voci) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<PacchettiService>();

        var offerta = await servizio.NuovaOffertaAsync(id, pacchetto);
        offerta.NomeFornitore = "Padilla";
        offerta.Data = new DateOnly(2026, 2, 12);
        offerta.ValiditaGiorni = 14;
        offerta.TotaleDichiarato = 10 * 298 * 0.57m + 10 * 12 * 0.57m + 2 * 600 * 0.57m + 490m;
        // Porta, cilindro e maniglione: tre righe per le stesse voci del computo.
        offerta.Righe.Add(new RigaOfferta { Id = Guid.Empty, Descrizione = "Porta REI60 L.890", Quantita = 10, PrezzoListino = 298, ScontoPercentuale = 43, VoceComputoId = voci[0].Id });
        offerta.Righe.Add(new RigaOfferta { Id = Guid.Empty, Descrizione = "Cilindro Yale 80mm", Quantita = 10, PrezzoListino = 12, ScontoPercentuale = 43, VoceComputoId = voci[0].Id });
        offerta.Righe.Add(new RigaOfferta { Id = Guid.Empty, Descrizione = "Porta REI60 due ante", Quantita = 2, PrezzoListino = 600, ScontoPercentuale = 43, VoceComputoId = voci[1].Id });
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Trasporto", Importo = 490 });
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Scarico", Importo = 999, Incluso = true });
        await servizio.SalvaOffertaAsync(id, offerta);

        var d = (await servizio.DettaglioAsync(id, pacchetto))!;
        var o = d.Scelta!;
        Assert.Equal(new DateOnly(2026, 2, 26), o.Offerta.Scadenza);
        Assert.Equal(1767m, o.Risultato.Voci[0].Costo);          // 1.698,60 + 68,40
        Assert.Equal(176.7m, o.Risultato.Voci[0].CostoUnitario);
        Assert.Equal(684m, o.Risultato.Voci[1].Costo);
        Assert.Equal(490m, o.Risultato.CostoACorpo);
        Assert.Equal(2941m, o.Risultato.Costo);
        Assert.Equal(o.Offerta.TotaleDichiarato, o.Risultato.TotaleOffertaCalcolato);
        Assert.Equal((10 * 600 + 2 * 900) * 0.8m, o.Risultato.RicavoNetto);

        // Modifica: si toglie una riga e si cambia lo sconto di un'altra.
        var modifica = (await servizio.OffertaAsync(id, o.Offerta.Id))!;
        modifica.Righe.RemoveAt(1);
        modifica.Righe[0].ScontoPercentuale = 50;
        await servizio.SalvaOffertaAsync(id, modifica);
        var dopo = (await servizio.DettaglioAsync(id, pacchetto))!.Scelta!;
        Assert.Equal(2, dopo.Offerta.Righe.Count);
        Assert.Equal(1490m, dopo.Risultato.Voci[0].Costo);
    }

    [Fact]
    public async Task Quantita_di_pianificazione_richiede_la_nota_e_alimenta_ricavo_e_costo()
    {
        var (amb, id, pacchetto, voci) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<PacchettiService>();
        var offerta = await servizio.NuovaOffertaAsync(id, pacchetto);
        var ov = offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id);
        ov.QuantitaPianificazione = 12;
        ov.FornituraEPosa = 400;

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => servizio.SalvaOffertaAsync(id, offerta));
        ov.NotaQuantita = "Rilievo in cantiere: 12 porte";
        await servizio.SalvaOffertaAsync(id, offerta);

        var r = (await servizio.DettaglioAsync(id, pacchetto))!.Scelta!.Risultato;
        Assert.True(r.Voci[0].QuantitaModificata);
        Assert.Equal(12 * 600 * 0.8m, r.Voci[0].Ricavo.Netto);
        Assert.Equal(4800m, r.Voci[0].Costo);
    }

    [Fact]
    public async Task Una_voce_appartiene_a_un_solo_pacchetto_e_si_puo_spostare()
    {
        var (amb, id, pacchetto, voci) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<PacchettiService>();
        var altro = await servizio.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte a due ante" });

        Assert.Equal(1, await servizio.AssegnaVociAsync(id, altro, [voci[1].Id]));
        Assert.Single((await servizio.DettaglioAsync(id, pacchetto))!.Voci);
        Assert.Single((await servizio.DettaglioAsync(id, altro))!.Voci);
        Assert.Empty(await servizio.VociLibereAsync(id));

        await servizio.RimuoviVoceAsync(id, altro, voci[1].Id);
        Assert.Single(await servizio.VociLibereAsync(id));
        var t = await servizio.TabelloneAsync(id);
        Assert.Equal(1, t.VociNonAssegnate);
        Assert.Equal(1440m, t.RicavoNonAssegnato);
    }

    [Fact]
    public async Task Tabellone_con_offerta_scelta_note_e_stato()
    {
        var (amb, id, pacchetto, voci) = await PreparaAsync();
        await using var _ = amb;
        var servizio = amb.Get<PacchettiService>();
        var a = await servizio.NuovaOffertaAsync(id, pacchetto);
        a.NomeFornitore = "A";
        foreach (var v in a.Voci) v.Fornitura = 300;
        await servizio.SalvaOffertaAsync(id, a);
        var b = await servizio.NuovaOffertaAsync(id, pacchetto);
        b.NomeFornitore = "B";
        foreach (var v in b.Voci) v.Fornitura = 250;
        var idB = await servizio.SalvaOffertaAsync(id, b);
        await servizio.AggiungiNotaAsync(id, pacchetto, new DateOnly(2026, 10, 22), "dgs", "In attesa del preventivo");

        var riga = (await servizio.TabelloneAsync(id)).Righe.Single();
        Assert.Equal("A", riga.Fornitore);   // la prima offerta resta quella di riferimento
        Assert.Equal(3600m, riga.Costo);

        await servizio.ScegliOffertaAsync(id, pacchetto, riga.Versione, idB);
        riga = (await servizio.TabelloneAsync(id)).Righe.Single();
        Assert.Equal("B", riga.Fornitore);
        Assert.Equal(3000m, riga.Costo);
        Assert.Equal(6240m - 3000m, riga.Mdc);
        Assert.Equal("DGS", riga.UltimaNota!.Iniziali);
    }
}
