using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>Modulo 2, rilascio 2A: ordini dalla Lista della Spesa, ordini parziali, Budget 0 e blocco dei prezzi contrattuali.</summary>
public class Modulo2Tests
{
    private sealed record Commessa(AmbienteDiProva Amb, Guid Id, Guid Porte, Guid Massetti) : IAsyncDisposable
    {
        public T Get<T>() where T : notnull => Amb.Get<T>();
        public ValueTask DisposeAsync() => Amb.DisposeAsync();
    }

    /// <summary>
    /// «Porte» (fornitura Padilla: A 10 cad × 400, B 2 cad × 500, trasporto 150) e «Massetti» in esecuzione diretta con stima
    /// interna analitica: voce C 100 m² con cemento 0,2 q/m² × 12 € e manodopera 0,5 h/m² × 30 €.
    /// </summary>
    private static async Task<Commessa> PreparaAsync(bool conCliente = true)
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        Guid? cliente = conCliente ? (await amb.Get<ClientiService>().SalvaAsync(new DatiCliente { Denominazione = "Comune di Caltagirone" })).Id : null;
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova", cliente)).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.ImpostaRibassoAsync(id, (await prv.StatoAsync(id)).Versione, 10);
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "A", Descrizione = "Porta a un'anta", Unita = "cad", Quantita = 10, Prezzo = 600 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "B", Descrizione = "Porta a due ante", Unita = "cad", Quantita = 2, Prezzo = 900 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "C", Descrizione = "Massetto", Unita = "m²", Quantita = 100, Prezzo = 40 });
        var voci = await prv.VociAsync(id);

        var pacchetti = amb.Get<PacchettiService>();
        var porte = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Porte", Tipo = TipoAffidamento.Fornitura });
        await pacchetti.AssegnaVociAsync(id, porte, [voci[0].Id, voci[1].Id]);
        var offerta = await pacchetti.NuovaOffertaAsync(id, porte);
        offerta.NomeFornitore = "Padilla";
        offerta.Tipo = TipoOfferta.SolaFornitura;
        offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id).Fornitura = 400;
        offerta.Voci.Single(v => v.VoceComputoId == voci[1].Id).Fornitura = 500;
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Trasporto", Importo = 150 });
        await pacchetti.SalvaOffertaAsync(id, offerta);

        var massetti = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Massetti", Tipo = TipoAffidamento.EsecuzioneDiretta });
        await pacchetti.AssegnaVociAsync(id, massetti, [voci[2].Id]);
        var stima = await pacchetti.NuovaOffertaAsync(id, massetti);
        stima.Tipo = TipoOfferta.StimaInterna;
        var ov = stima.Voci.Single();
        ov.Risorse.Add(new RisorsaVoce { Id = Guid.Empty, Ordine = 1, Categoria = CategoriaRisorsa.Materiali, Descrizione = "Cemento", Unita = "q", Fabbisogno = 0.2m, Prezzo = 12 });
        ov.Risorse.Add(new RisorsaVoce { Id = Guid.Empty, Ordine = 2, Categoria = CategoriaRisorsa.Manodopera, Descrizione = "Operaio", Unita = "h", Fabbisogno = 0.5m, Prezzo = 30 });
        await pacchetti.SalvaOffertaAsync(id, stima);
        return new Commessa(amb, id, porte, massetti);
    }

    private static async Task<RigaSpesa> RigaAsync(Commessa c, string descrizione) =>
        (await c.Get<ListaSpesaService>().CalcolaAsync(c.Id)).Righe.Single(r => r.Descrizione == descrizione);

    [Fact]
    public async Task Due_ordini_parziali_e_pacchetto_definitivo_solo_al_secondo()
    {
        await using var c = await PreparaAsync();
        var ordini = c.Get<OrdiniService>();
        var portaA = await RigaAsync(c, "Porta a un'anta");

        var bozzaId = await ordini.PreparaDaListaAsync(c.Id, [portaA.ChiaveOrdine]);
        var bozza = (await ordini.OttieniAsync(c.Id, bozzaId))!;
        var riga = Assert.Single(bozza.Righe);
        Assert.Equal(10m, riga.Quantita);
        Assert.Equal(400m, riga.Prezzo);
        Assert.Equal(c.Porte, bozza.PacchettoId);
        riga.Quantita = 6;                                       // consegna frazionata
        await ordini.SalvaBozzaAsync(c.Id, bozza);
        var primo = await ordini.EmettiAsync(c.Id, bozzaId);
        Assert.Equal(2_400m, primo.Totale);

        portaA = await RigaAsync(c, "Porta a un'anta");
        Assert.Equal(6m, portaA.QuantitaOrdinata);
        Assert.Equal(4m, portaA.DaOrdinare);
        Assert.Equal(portaA.Quantita, portaA.QuantitaOrdinata + portaA.DaOrdinare);
        var dettaglio = (await c.Get<PacchettiService>().DettaglioAsync(c.Id, c.Porte))!.Pacchetto;
        Assert.Equal(StatoCosto.Provvisorio, dettaglio.StatoCosto);
        var riga1 = (await c.Get<PacchettiService>().TabelloneAsync(c.Id)).Righe.Single(r => r.Id == c.Porte);
        Assert.True(riga1.HaOrdini);
        Assert.Equal(Math.Round(2_400m / 5_150m * 100m, 4), Math.Round(riga1.PercentualeOrdinata, 4));
        Assert.Equal(5_150m, riga1.Costo);                      // 2.400 ordinati + 2.750 ancora da ordinare

        // Il secondo ordine dal pacchetto propone solo il residuo.
        var secondoId = await ordini.PreparaDaPacchettoAsync(c.Id, c.Porte);
        var secondo = (await ordini.OttieniAsync(c.Id, secondoId))!;
        Assert.Equal(4m, secondo.Righe.Single(r => r.Descrizione == "Porta a un'anta").Quantita);
        Assert.Equal(2_750m, secondo.Totale);                   // 4 × 400 + 2 × 500 + trasporto 150
        await ordini.EmettiAsync(c.Id, secondoId);

        dettaglio = (await c.Get<PacchettiService>().DettaglioAsync(c.Id, c.Porte))!.Pacchetto;
        Assert.Equal(StatoCosto.Definitivo, dettaglio.StatoCosto);
        Assert.Equal("ODA 2026/001, ODA 2026/002", dettaglio.RiferimentoOda);
        var lista = await c.Get<ListaSpesaService>().CalcolaAsync(c.Id);
        Assert.All(lista.Righe.Where(r => r.PacchettoId == c.Porte), r => Assert.Equal(0m, r.DaOrdinare));
        Assert.Equal(["ODA 2026/001", "ODA 2026/002"], lista.Righe.Single(r => r.Descrizione == "Porta a un'anta").Ordini);
    }

    [Fact]
    public async Task Righe_da_assegnare_di_due_pacchetti_con_fornitore_scelto_e_annullamento()
    {
        await using var c = await PreparaAsync();
        var pacchetti = c.Get<PacchettiService>();
        // Anche le porte diventano una stima interna: righe «Da assegnare» in due pacchetti.
        var d = (await pacchetti.DettaglioAsync(c.Id, c.Porte))!;
        var offerta = (await pacchetti.OffertaAsync(c.Id, d.Scelta!.Offerta.Id))!;
        offerta.Tipo = TipoOfferta.StimaInterna;
        await pacchetti.SalvaOffertaAsync(c.Id, offerta);

        var lista = await c.Get<ListaSpesaService>().CalcolaAsync(c.Id);
        var cemento = lista.Righe.Single(r => r.Descrizione == "Cemento");
        var portaB = lista.Righe.Single(r => r.Descrizione == "Porta a due ante");
        Assert.Equal(RigaSpesa.EsecuzioneDiretta, cemento.Fornitore);
        Assert.Equal(RigaSpesa.DaAssegnare, portaB.Fornitore);

        var ordini = c.Get<OrdiniService>();
        var bozzaId = await ordini.PreparaDaListaAsync(c.Id, [cemento.ChiaveOrdine, portaB.ChiaveOrdine]);
        var bozza = (await ordini.OttieniAsync(c.Id, bozzaId))!;
        Assert.Null(bozza.PacchettoId);
        Assert.Equal(2, bozza.Righe.Count);
        var fornitore = await c.Get<FornitoriService>().SalvaAsync(new DatiFornitore { Denominazione = "Rivendita Edile Sud" });
        bozza.FornitoreId = fornitore.Id;
        // Riga libera di trasporto imputata ai massetti.
        bozza.Righe.Add(new RigaOrdine { Id = Guid.Empty, Ordine = 3, Descrizione = "Trasporto in cantiere", Unita = "a corpo", Quantita = 1, Prezzo = 80, PacchettoId = c.Massetti });
        await ordini.SalvaBozzaAsync(c.Id, bozza);
        var o = await ordini.EmettiAsync(c.Id, bozzaId);
        Assert.Equal("Rivendita Edile Sud", o.Fornitore);
        Assert.Equal(20m * 12 + 2 * 500 + 80, o.Totale);

        var tab = await pacchetti.TabelloneAsync(c.Id);
        Assert.Equal(240m + 1_500m + 80, tab.Righe.Single(r => r.Id == c.Massetti).Costo);   // cemento ordinato + manodopera + trasporto
        Assert.Equal((await RigaAsync(c, "Cemento")).Quantita, (await RigaAsync(c, "Cemento")).QuantitaOrdinata);

        await ordini.AnnullaAsync(c.Id, o.Id, "Fornitore sostituito");
        Assert.Equal(0m, (await RigaAsync(c, "Cemento")).QuantitaOrdinata);
        Assert.Equal(2m, (await RigaAsync(c, "Porta a due ante")).DaOrdinare);
        Assert.Equal(1_740m, (await pacchetti.TabelloneAsync(c.Id)).Righe.Single(r => r.Id == c.Massetti).Costo);
    }

    [Fact]
    public async Task Un_ordine_raccoglie_righe_di_un_solo_fornitore()
    {
        await using var c = await PreparaAsync();
        var lista = await c.Get<ListaSpesaService>().CalcolaAsync(c.Id);
        var pacchetti = c.Get<PacchettiService>();
        var altro = await pacchetti.SalvaPacchettoAsync(c.Id, new Pacchetto { Descrizione = "Infissi" });
        var voce = await c.Get<PreventivoService>().SalvaVoceAsync2(c.Id, "D", "Finestra", 4, 700);
        await pacchetti.AssegnaVociAsync(c.Id, altro, [voce]);
        var o = await pacchetti.NuovaOffertaAsync(c.Id, altro);
        o.NomeFornitore = "Serramenti Rossi";
        o.Tipo = TipoOfferta.SolaFornitura;
        o.Voci.Single().Fornitura = 450;
        await pacchetti.SalvaOffertaAsync(c.Id, o);

        lista = await c.Get<ListaSpesaService>().CalcolaAsync(c.Id);
        var chiavi = lista.Righe.Where(r => r.Descrizione is "Finestra" or "Porta a un'anta").Select(r => r.ChiaveOrdine).ToList();
        var ex = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => c.Get<OrdiniService>().PreparaDaListaAsync(c.Id, chiavi));
        Assert.Contains("un solo fornitore", ex.Message);
    }

    [Fact]
    public async Task Quantita_oltre_il_pianificato_e_prezzo_diverso_richiedono_una_nota()
    {
        await using var c = await PreparaAsync();
        var ordini = c.Get<OrdiniService>();
        var bozzaId = await ordini.PreparaDaListaAsync(c.Id, [(await RigaAsync(c, "Porta a un'anta")).ChiaveOrdine]);
        var bozza = (await ordini.OttieniAsync(c.Id, bozzaId))!;
        bozza.Righe[0].Quantita = 12;
        bozza.Righe[0].Prezzo = 380;
        await ordini.SalvaBozzaAsync(c.Id, bozza);
        var verifica = await ordini.VerificaAsync(c.Id, bozzaId);
        Assert.Equal(2, verifica.Impedimenti.Count);
        Assert.True(verifica.Righe.Single().Eccedenza);
        Assert.True(verifica.Righe.Single().PrezzoDiverso);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ordini.EmettiAsync(c.Id, bozzaId));

        bozza = (await ordini.OttieniAsync(c.Id, bozzaId))!;
        bozza.Righe[0].NotaScostamento = "Due porte di scorta; prezzo trattato";
        await ordini.SalvaBozzaAsync(c.Id, bozza);
        var o = await ordini.EmettiAsync(c.Id, bozzaId);
        Assert.Equal(4_560m, o.Totale);

        var a = await RigaAsync(c, "Porta a un'anta");
        Assert.True(a.Eccedenza);
        Assert.Equal(0m, a.DaOrdinare);
        // Costo attuale = ordine (4.560) + porta B e trasporto ancora da ordinare (1.150).
        Assert.Equal(5_710m, (await c.Get<PacchettiService>().TabelloneAsync(c.Id)).Righe.Single(r => r.Id == c.Porte).Costo);
    }

    [Fact]
    public async Task Bozza_eliminabile_ordine_emesso_no()
    {
        await using var c = await PreparaAsync();
        var ordini = c.Get<OrdiniService>();
        var bozzaId = await ordini.PreparaDaPacchettoAsync(c.Id, c.Porte);
        await ordini.EliminaBozzaAsync(c.Id, bozzaId);
        Assert.Null(await ordini.OttieniAsync(c.Id, bozzaId));

        var o = await ordini.EmettiAsync(c.Id, await ordini.PreparaDaPacchettoAsync(c.Id, c.Porte));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ordini.EliminaBozzaAsync(c.Id, o.Id));
        Assert.Equal("ODA 2026/001", o.Numero);   // la bozza eliminata non ha consumato numeri
    }

    [Fact]
    public async Task Budget_0_proposto_respinto_riproposto_e_approvato_poi_prezzi_bloccati()
    {
        await using var c = await PreparaAsync();
        var budget = c.Get<BudgetService>();
        var admin = new IdentitaUtente(Guid.NewGuid(), "salvatore", "Salvatore", RuoloUtente.Amministratore);
        var operatore = new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);

        var stato = await budget.StatoAsync(c.Id);
        Assert.True(stato.Proponibile);
        Assert.Equal(0, stato.ProssimoNumero);
        Assert.Contains(stato.Controlli, k => k.Bloccante && k.Superato && k.Descrizione.StartsWith("Totale della Lista"));

        VersionePreventivo proposta;
        using (UtenteCorrente.Imposta(operatore)) proposta = await budget.ProponiAsync(c.Id, null);
        Assert.Equal(0, proposta.NumeroBudget);
        Assert.Equal(StatoBudget.InApprovazione, proposta.StatoBudget);
        Assert.Equal("Mario Rossi", proposta.CreatoDa);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => budget.ProponiAsync(c.Id, null));

        using (UtenteCorrente.Imposta(operatore))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => budget.ApprovaAsync(c.Id, proposta.Id));
        using (UtenteCorrente.Imposta(admin))
        {
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => budget.RespingiAsync(c.Id, proposta.Id, ""));
            await budget.RespingiAsync(c.Id, proposta.Id, "Manca l'offerta degli intonaci");
        }

        // Tabellone in approvazione: nessun budget di riferimento dopo il rifiuto.
        Assert.Null((await c.Get<PacchettiService>().TabelloneAsync(c.Id)).Budget);

        var seconda = await budget.ProponiAsync(c.Id, "Budget di avvio");
        Assert.Equal(0, seconda.NumeroBudget);
        Assert.Equal("Budget 0 in approvazione", (await c.Get<PacchettiService>().TabelloneAsync(c.Id)).Budget);
        using (UtenteCorrente.Imposta(admin)) await budget.ApprovaAsync(c.Id, seconda.Id);

        stato = await budget.StatoAsync(c.Id);
        Assert.Equal(seconda.Id, stato.Approvato!.Id);
        Assert.Equal("Salvatore", stato.Approvato.EsitoDa);
        Assert.False(stato.Proponibile);
        Assert.Contains("perizia", stato.MotivoNonProponibile);
        Assert.Equal(2, stato.Budget.Count);   // anche il respinto resta nello storico

        // Budget immutabile.
        await using (var db = await c.Get<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync())
        {
            var v = await db.Versioni.FirstAsync(x => x.Id == seconda.Id);
            v.StatoBudget = StatoBudget.Respinto;
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => db.SaveChangesAsync());
        }

        // Prezzi contrattuali bloccati nella bozza.
        var prv = c.Get<PreventivoService>();
        var statoPrv = await prv.StatoAsync(c.Id);
        Assert.Equal("Budget 0 approvato", statoPrv.BloccoBudget);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => prv.ImpostaRibassoAsync(c.Id, statoPrv.Versione, 12));
        var voce = (await prv.VociAsync(c.Id))[0];
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => prv.SalvaVoceAsync(c.Id, new DatiVoce
        {
            Id = voce.Id, Versione = voce.Versione, Tariffa = voce.Tariffa, Descrizione = voce.Descrizione, Unita = voce.Unita,
            Quantita = voce.Quantita, Prezzo = voce.Prezzo + 1,
        }));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => prv.SalvaVoceAsync(c.Id, new DatiVoce { Tariffa = "Z", Descrizione = "Nuova", Quantita = 1, Prezzo = 1 }));

        // Un ordine a prezzo diverso cambia il costo attuale ma non il Budget 0.
        var ordini = c.Get<OrdiniService>();
        var bozzaId = await ordini.PreparaDaPacchettoAsync(c.Id, c.Porte);
        var bozza = (await ordini.OttieniAsync(c.Id, bozzaId))!;
        var a = bozza.Righe.Single(r => r.Descrizione == "Porta a un'anta");
        a.Prezzo = 380;
        a.NotaScostamento = "Sconto ottenuto in trattativa";
        await ordini.SalvaBozzaAsync(c.Id, bozza);
        await ordini.EmettiAsync(c.Id, bozzaId);

        var tab = await c.Get<PacchettiService>().TabelloneAsync(c.Id);
        var porte = tab.Righe.Single(r => r.Id == c.Porte);
        Assert.Equal("Budget 0 approvato", tab.Budget);
        Assert.Equal(5_150m, porte.CostoBudget);
        Assert.Equal(4_950m, porte.Costo);
        Assert.Equal(-200m, porte.Scostamento);
        Assert.Equal(5_150m, porte.CostoPianificato);
        Assert.Equal(seconda.Costo, (await budget.StatoAsync(c.Id)).Approvato!.Costo);
        Assert.Equal(seconda.Costo - 200m, (await c.Get<RiepilogoService>().FotografiaAsync(c.Id)).Totali.Costo);
    }

    [Fact]
    public async Task Budget_bloccato_senza_cliente_o_con_voci_non_assegnate()
    {
        await using var c = await PreparaAsync(conCliente: false);
        var budget = c.Get<BudgetService>();
        await c.Get<PreventivoService>().SalvaVoceAsync(c.Id, new DatiVoce { Tariffa = "E", Descrizione = "Voce libera", Quantita = 1, Prezzo = 10 });
        var stato = await budget.StatoAsync(c.Id);
        Assert.False(stato.Proponibile);
        Assert.Contains(stato.Controlli, k => k.Blocca && k.Descrizione.Contains("cliente"));
        Assert.Contains(stato.Controlli, k => k.Blocca && k.Dettagli.Any(d => d.Contains("Voce libera")));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => budget.ProponiAsync(c.Id, null));
    }

    [Fact]
    public async Task Consultazione_nessuna_modifica_operatore_non_abilitato_solo_bozze()
    {
        await using var c = await PreparaAsync();
        var ordini = c.Get<OrdiniService>();
        var lettore = new IdentitaUtente(Guid.NewGuid(), "anna", "Anna Verdi", RuoloUtente.Consultazione);
        var operatore = new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);
        var abilitato = operatore with { PuoEmettereOrdini = true };

        using (UtenteCorrente.Imposta(lettore))
        {
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ordini.PreparaDaPacchettoAsync(c.Id, c.Porte));
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => c.Get<BudgetService>().ProponiAsync(c.Id, null));
        }
        Guid bozzaId;
        using (UtenteCorrente.Imposta(operatore))
        {
            bozzaId = await ordini.PreparaDaPacchettoAsync(c.Id, c.Porte);
            Assert.False((await ordini.VerificaAsync(c.Id, bozzaId)).PuoEmettere);
            var ex = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => ordini.EmettiAsync(c.Id, bozzaId));
            Assert.Contains("abilitati", ex.Message);
        }
        using (UtenteCorrente.Imposta(abilitato))
        {
            var o = await ordini.EmettiAsync(c.Id, bozzaId);
            Assert.Equal("Mario Rossi", o.EmessoDa);
        }
    }

    [Fact]
    public async Task Ordine_emesso_prima_del_modulo_2_copre_tutto_il_pacchetto()
    {
        await using var c = await PreparaAsync();
        await using (var db = await c.Get<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync())
        {
            var o = new OrdineAcquisto
            {
                CommessaId = c.Id, PacchettoId = c.Porte, Anno = 2026, Progressivo = 1, Numero = "ODA 2026/001",
                Data = new DateOnly(2026, 10, 22), Fornitore = "Padilla", Oggetto = "Porte", Totale = 5_100, Stato = StatoOrdine.Emesso,
            };
            o.Righe.Add(new RigaOrdine { OrdineId = o.Id, CommessaId = c.Id, Ordine = 1, Descrizione = "Porte", Quantita = 1, Prezzo = 5_100, Importo = 5_100 });
            db.Ordini.Add(o);
            await db.SaveChangesAsync();
        }
        var lista = await c.Get<ListaSpesaService>().CalcolaAsync(c.Id);
        Assert.All(lista.Righe.Where(r => r.PacchettoId == c.Porte), r => Assert.Equal(0m, r.DaOrdinare));
        var porte = (await c.Get<PacchettiService>().TabelloneAsync(c.Id)).Righe.Single(r => r.Id == c.Porte);
        Assert.Equal(5_100m, Math.Round(porte.Costo!.Value, 2));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => c.Get<OrdiniService>().PreparaDaPacchettoAsync(c.Id, c.Porte));
    }
}

internal static class EstensioniProva
{
    public static async Task<Guid> SalvaVoceAsync2(this PreventivoService prv, Guid commessaId, string tariffa, string descrizione, decimal quantita, decimal prezzo)
    {
        await prv.SalvaVoceAsync(commessaId, new DatiVoce { Tariffa = tariffa, Descrizione = descrizione, Unita = "cad", Quantita = quantita, Prezzo = prezzo });
        return (await prv.VociAsync(commessaId)).Single(v => v.Tariffa == tariffa).Id;
    }
}
