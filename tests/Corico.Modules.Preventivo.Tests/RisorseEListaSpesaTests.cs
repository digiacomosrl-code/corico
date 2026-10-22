using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class RisorseEListaSpesaTests
{
    /// <summary>Intonaco (100 mq × 20 €) e tinteggiatura (200 mq × 8 €) nello stesso pacchetto, ribasso 0.</summary>
    private static async Task<(AmbienteDiProva Amb, Guid CommessaId, Guid PacchettoId, IReadOnlyList<RigaVoce> Voci)> PreparaAsync()
    {
        var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa risorse")).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "INT", Descrizione = "Intonaco civile", Unita = "mq", Quantita = 100, Prezzo = 20 });
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "TIN", Descrizione = "Tinteggiatura", Unita = "mq", Quantita = 200, Prezzo = 8 });
        var voci = await prv.VociAsync(id);
        var pacchetti = amb.Get<PacchettiService>();
        var pacchetto = await pacchetti.SalvaPacchettoAsync(id, new Pacchetto { Descrizione = "Finiture", Tipo = TipoAffidamento.EsecuzioneDiretta });
        await pacchetti.AssegnaVociAsync(id, pacchetto, voci.Select(v => v.Id).ToList());
        return (amb, id, pacchetto, voci);
    }

    private static RigaAnalisiAziendale Riga(CategoriaRisorsa c, string d, string um, decimal f, decimal p, decimal sfrido = 0) =>
        new() { Categoria = c, Descrizione = d, Unita = um, Fabbisogno = f, Prezzo = p, SfridoPercentuale = sfrido };

    [Fact]
    public async Task Livello_analitico_analisi_e_lista_della_spesa()
    {
        var (amb, id, pacchetto, voci) = await PreparaAsync();
        await using var _ = amb;
        var analisi = amb.Get<AnalisiService>();

        // Analisi aziendale dell'intonaco: 0,02 mc di malta con sfrido 10% a 150 €/mc e 0,5 h di manodopera a 30 €/h → 18,30 €/mq.
        var a = new AnalisiAziendale { Id = Guid.Empty, Codice = "AN.01", Descrizione = "Intonaco civile", Unita = "mq" };
        a.Righe.Add(Riga(CategoriaRisorsa.Materiali, "Malta premiscelata", "mc", 0.02m, 150, 10));
        a.Righe.Add(Riga(CategoriaRisorsa.Manodopera, "Operaio qualificato", "h", 0.5m, 30));
        var idAnalisi = await analisi.SalvaAsync(a);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => analisi.RisorseDaApplicareAsync(idAnalisi, id));
        await analisi.CambiaStatoAsync(idAnalisi, StatoAnalisi.Confermata);
        Assert.Equal(18.30m, (await analisi.ElencoAsync(soloConfermate: true)).Single().CostoUnitario);

        // Offerta interna: intonaco a livello analitico (con colonne sintetiche ignorate), tinteggiatura sintetica.
        var pacchetti = amb.Get<PacchettiService>();
        var offerta = await pacchetti.NuovaOffertaAsync(id, pacchetto);
        offerta.Tipo = TipoOfferta.StimaInterna;
        var intonaco = offerta.Voci.Single(v => v.VoceComputoId == voci[0].Id);
        intonaco.Fornitura = 99;
        intonaco.Risorse.AddRange(await analisi.RisorseDaApplicareAsync(idAnalisi, id));
        var tinta = offerta.Voci.Single(v => v.VoceComputoId == voci[1].Id);
        (tinta.Fornitura, tinta.ManodoperaENoli) = (1.5m, 3m);
        offerta.CostiACorpo.Add(new CostoACorpo { Id = Guid.Empty, Descrizione = "Trasporto a discarica", Importo = 250 });
        await pacchetti.SalvaOffertaAsync(id, offerta);

        var scelta = (await pacchetti.DettaglioAsync(id, pacchetto))!.Scelta!;
        Assert.True(scelta.Risultato.Voci[0].Analitico);
        Assert.Equal(1_830m, scelta.Risultato.Voci[0].Costo);              // 100 × 18,30
        Assert.Equal(900m, scelta.Risultato.Voci[1].Costo);                // 200 × (1,5 + 3)
        Assert.Equal(2_980m, scelta.Risultato.Costo);                      // + trasporto 250
        Assert.All(scelta.Offerta.Voci[0].Risorse, r => Assert.Equal(idAnalisi, r.AnalisiId));

        // La modifica successiva dell'analisi non cambia l'offerta.
        var modificata = (await analisi.OttieniAsync(idAnalisi))!;
        modificata.Righe[1].Prezzo = 40;
        await analisi.SalvaAsync(modificata);
        Assert.Equal(2_980m, (await pacchetti.DettaglioAsync(id, pacchetto))!.Scelta!.Risultato.Costo);

        // Lista della Spesa: stesso totale del costo pianificato, ripartito nelle categorie.
        var lista = await amb.Get<ListaSpesaService>().CalcolaAsync(id);
        Assert.Equal(2_980m, lista.Totale);
        Assert.Equal((await amb.Get<RiepilogoService>().FotografiaAsync(id)).Totali.Costo, lista.Totale);
        var perCategoria = lista.PerCategoria.ToDictionary(x => x.Categoria, x => x.Importo);
        Assert.Equal(330m + 300m, perCategoria[CategoriaRisorsa.Materiali]);        // malta 100 × 0,022 × 150 + tinta 200 × 1,5
        Assert.Equal(1_500m + 600m, perCategoria[CategoriaRisorsa.Manodopera]);     // 100 × 0,5 × 30 + 200 × 3
        Assert.Equal(250m, perCategoria[CategoriaRisorsa.TrasportiESmaltimenti]);
        var malta = lista.Righe.Single(r => r.Descrizione == "Malta premiscelata");
        Assert.Equal(2.2m, malta.Quantita);                                         // mc da acquistare, sfrido compreso
        Assert.Equal(OrigineRigaSpesa.Risorsa, malta.Origine);

        // Salvare come analisi le risorse di una voce crea una nuova analisi in bozza.
        var nuova = await analisi.CreaDaRisorseAsync("Intonaco da commessa", "mq", scelta.Offerta.Voci[0].Risorse, "Commessa risorse");
        Assert.Equal(StatoAnalisi.Bozza, (await analisi.OttieniAsync(nuova))!.Stato);

        var excel = (await amb.Get<EsportazioniService>().ListaSpesaExcelAsync(id))!;
        Assert.True(excel.Contenuto.Length > 1000);
    }

    [Fact]
    public void Categorie_delle_righe_e_delle_voci_a_corpo()
    {
        Assert.Equal(CategoriaRisorsa.Materiali, ListaSpesaService.CategoriaRighe(TipoOfferta.SolaFornitura));
        Assert.Equal(CategoriaRisorsa.Subappalti, ListaSpesaService.CategoriaRighe(TipoOfferta.FornituraEPosa));
        Assert.Equal(CategoriaRisorsa.Attrezzature, ListaSpesaService.CategoriaRighe(TipoOfferta.Nolo));
        Assert.Equal(CategoriaRisorsa.TrasportiESmaltimenti, ListaSpesaService.CategoriaCorpo("Scarico con muletto"));
        Assert.Equal(CategoriaRisorsa.AltriCosti, ListaSpesaService.CategoriaCorpo("Installazione"));
    }
}
