using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Tests;

public class OrdiniTests
{
    /// <summary>Pacchetto "Porte": voci A (10 cad, Fornitura 400) e B (2 cad, Fornitura 500), riga d'offerta 1 × 200 sconto 10%, trasporto a corpo 150.</summary>
    private static async Task<(AmbienteDiProva Amb, Guid CommessaId, Guid PacchettoId)> PreparaAsync()
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
        offerta.Pagamento = "60 gg d.f.f.m.";
        offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id).Fornitura = 400;
        offerta.Voci.Single(v => v.VoceComputoId == voci[1].Id).Fornitura = 500;
        offerta.Righe.Add(new RigaOfferta { Id = Guid.Empty, Descrizione = "Maniglioni antipanico", Unita = "cad", Quantita = 1, PrezzoListino = 200, ScontoPercentuale = 10 });
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Trasporto", Importo = 150 });
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Imballo", Incluso = true });
        await pacchetti.SalvaOffertaAsync(id, offerta);
        return (amb, id, pacchetto);
    }

    [Fact]
    public async Task Emissione_dal_pacchetto_con_righe_della_lista_e_pacchetto_definitivo()
    {
        var (amb, id, pacchetto) = await PreparaAsync();
        await using var _ = amb;
        var ordini = amb.Get<OrdiniService>();

        var bozzaId = await ordini.PreparaDaPacchettoAsync(id, pacchetto);
        var bozza = (await ordini.OttieniAsync(id, bozzaId))!;
        Assert.Equal(StatoOrdine.Bozza, bozza.Stato);
        Assert.Equal("", bozza.Numero);
        Assert.Equal("Padilla", bozza.Fornitore);
        Assert.True(bozza.FornitoreDaOfferta);
        Assert.Equal(4, bozza.Righe.Count);                 // 2 voci, 1 riga d'offerta, trasporto (l'imballo incluso non si ordina)
        Assert.Equal(5_330m, bozza.Totale);                 // 4.000 + 1.000 + 180 + 150
        var costo = (await amb.Get<PacchettiService>().DettaglioAsync(id, pacchetto))!.Scelta!.Risultato.Costo;
        Assert.Equal(costo, bozza.Totale);
        Assert.Equal("60 gg d.f.f.m.", bozza.Pagamento);
        Assert.Equal("Vs. offerta n. 123", bozza.RiferimentoOfferta);

        var verifica = await ordini.VerificaAsync(id, bozzaId);
        Assert.True(verifica.Emettibile);
        Assert.False(verifica.AziendaCompilata);
        Assert.Contains(verifica.Avvisi, a => a.Contains("Budget 0"));

        var pdfBozza = await amb.Get<EsportazioniService>().OrdinePdfAsync(id, bozzaId);
        Assert.StartsWith("Bozza", pdfBozza!.Nome);

        var o = await ordini.EmettiAsync(id, bozzaId);
        Assert.Equal("ODA 2026/001", o.Numero);
        Assert.Equal(new DateOnly(2026, 10, 21), o.Data);

        var p = (await amb.Get<PacchettiService>().DettaglioAsync(id, pacchetto))!.Pacchetto;
        Assert.Equal(StatoCosto.Definitivo, p.StatoCosto);
        Assert.Equal("ODA 2026/001", p.RiferimentoOda);

        // Tutto ordinato: non c'è più nulla da preparare.
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ordini.PreparaDaPacchettoAsync(id, pacchetto));

        var pdf = await amb.Get<EsportazioniService>().OrdinePdfAsync(id, o.Id);
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf!.Contenuto, 0, 4));
    }

    [Fact]
    public async Task Annullamento_con_motivo_e_nuovo_numero()
    {
        var (amb, id, pacchetto) = await PreparaAsync();
        await using var _ = amb;
        var ordini = amb.Get<OrdiniService>();
        var primo = await ordini.EmettiAsync(id, await ordini.PreparaDaPacchettoAsync(id, pacchetto));

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ordini.AnnullaAsync(id, primo.Id, " "));
        await ordini.AnnullaAsync(id, primo.Id, "Quantità errate");
        var p = (await amb.Get<PacchettiService>().DettaglioAsync(id, pacchetto))!.Pacchetto;
        Assert.Equal(StatoCosto.Provvisorio, p.StatoCosto);
        Assert.Null(p.RiferimentoOda);

        var secondo = await ordini.EmettiAsync(id, await ordini.PreparaDaPacchettoAsync(id, pacchetto));
        Assert.Equal("ODA 2026/002", secondo.Numero);

        var elenco = await ordini.ElencoAsync(id);
        Assert.Equal(["ODA 2026/002", "ODA 2026/001"], elenco.Select(o => o.Numero));
        Assert.Equal(StatoOrdine.Annullato, elenco[1].Stato);
        Assert.Equal("Quantità errate", elenco[1].MotivoAnnullamento);
    }

    [Fact]
    public async Task Ordine_emesso_non_modificabile()
    {
        var (amb, id, pacchetto) = await PreparaAsync();
        await using var _ = amb;
        var ordini = amb.Get<OrdiniService>();
        var o = await ordini.EmettiAsync(id, await ordini.PreparaDaPacchettoAsync(id, pacchetto));

        await using var db = await amb.Get<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync();
        var letto = await db.Ordini.Include(x => x.Righe).FirstAsync(x => x.Id == o.Id);
        letto.Totale = 1;
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var riga = await db.RigheOrdine.FirstAsync(r => r.OrdineId == o.Id);
        riga.Prezzo = 1;
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Ordini.Remove(await db.Ordini.FirstAsync(x => x.Id == o.Id));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => db.SaveChangesAsync());

        // Invio e conferma restano registrabili.
        await ordini.RegistraInvioAsync(id, o.Id, new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 23), null);
        Assert.Equal(new DateOnly(2026, 10, 23), (await ordini.OttieniAsync(id, o.Id))!.ConfermatoIl);
    }

    [Fact]
    public async Task Stima_interna_ordinabile_scegliendo_il_fornitore()
    {
        var (amb, id, pacchetto) = await PreparaAsync();
        await using var _ = amb;
        var pacchetti = amb.Get<PacchettiService>();
        var d = (await pacchetti.DettaglioAsync(id, pacchetto))!;
        var offerta = (await pacchetti.OffertaAsync(id, d.Scelta!.Offerta.Id))!;
        offerta.Tipo = TipoOfferta.StimaInterna;
        await pacchetti.SalvaOffertaAsync(id, offerta);

        var ordini = amb.Get<OrdiniService>();
        var bozzaId = await ordini.PreparaDaPacchettoAsync(id, pacchetto);
        var bozza = (await ordini.OttieniAsync(id, bozzaId))!;
        Assert.False(bozza.FornitoreDaOfferta);
        Assert.Equal("", bozza.Fornitore);
        var verifica = await ordini.VerificaAsync(id, bozzaId);
        Assert.False(verifica.Emettibile);
        Assert.Contains(verifica.Impedimenti, i => i.Contains("fornitore"));

        var f = await amb.Get<FornitoriService>().SalvaAsync(new DatiFornitore { Denominazione = "Edilnord S.r.l.", PartitaIva = "01234567890" });
        bozza.FornitoreId = f.Id;
        await ordini.SalvaBozzaAsync(id, bozza);
        var o = await ordini.EmettiAsync(id, bozzaId);
        Assert.Equal("Edilnord S.r.l.", o.Fornitore);
        Assert.Contains("P. IVA 01234567890", o.DatiFornitore);
    }

    [Fact]
    public async Task Assegnazioni_per_voce_dal_preventivo()
    {
        var (amb, id, pacchetto) = await PreparaAsync();
        await using var _ = amb;
        var pacchetti = amb.Get<PacchettiService>();
        var voci = await amb.Get<PreventivoService>().VociAsync(id);
        var nuovo = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte REI" });
        Assert.Equal(1, await pacchetti.AssegnaVociAsync(id, nuovo, [voci[1].Id]));

        var (elenco, perVoce) = await pacchetti.AssegnazioniAsync(id);
        Assert.Equal(["Porte", "Porte REI"], elenco.Select(p => p.Descrizione));
        Assert.Equal(pacchetto, perVoce[voci[0].Id]);
        Assert.Equal(nuovo, perVoce[voci[1].Id]);
    }

    [Fact]
    public async Task Lista_della_spesa_con_fornitore_o_da_assegnare()
    {
        var (amb, id, pacchetto) = await PreparaAsync();
        await using var _ = amb;
        var lista = await amb.Get<ListaSpesaService>().CalcolaAsync(id);
        Assert.All(lista.Righe, r => Assert.Equal("Padilla", r.Fornitore));

        var pacchetti = amb.Get<PacchettiService>();
        var d = (await pacchetti.DettaglioAsync(id, pacchetto))!;
        var offerta = (await pacchetti.OffertaAsync(id, d.Scelta!.Offerta.Id))!;
        offerta.Tipo = TipoOfferta.StimaInterna;
        await pacchetti.SalvaOffertaAsync(id, offerta);
        lista = await amb.Get<ListaSpesaService>().CalcolaAsync(id);
        Assert.All(lista.Righe, r => Assert.Equal(RigaSpesa.DaAssegnare, r.Fornitore));
        Assert.All(lista.Aggregata(), r => Assert.Equal(RigaSpesa.DaAssegnare, r.Fornitore));
    }

    [Fact]
    public async Task Lista_della_spesa_filtrata_per_fornitore_codice_e_descrizione()
    {
        var (amb, id, _) = await PreparaAsync();
        await using var _ = amb;
        var lista = await amb.Get<ListaSpesaService>().CalcolaAsync(id);
        Assert.Equal(["Padilla"], lista.Fornitori);
        Assert.Equal(lista.Righe.Count, lista.Filtra("padilla", null).Righe.Count);
        Assert.Empty(lista.Filtra(RigaSpesa.DaAssegnare, null).Righe);

        var porta = lista.Filtra(null, "porta");
        Assert.Equal(2, porta.Righe.Count);            // «Porta a un'anta» e «Porta a due ante»
        Assert.Single(lista.Filtra(null, "porta anta").Righe);
        Assert.Equal(5_000m, porta.Totale);
        var codice = lista.Righe.First(r => r.Descrizione == "Trasporto").Codice!;
        Assert.Equal("Trasporto", Assert.Single(lista.Filtra(null, codice.ToLowerInvariant()).Righe).Descrizione);

        var excel = await amb.Get<EsportazioniService>().ListaSpesaExcelAsync(id, null, "porta");
        Assert.NotNull(excel);
    }
}
