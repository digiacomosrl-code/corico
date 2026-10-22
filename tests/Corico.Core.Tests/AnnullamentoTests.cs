using Corico.Core.Dominio;
using Corico.Core.Servizi;

namespace Corico.Core.Tests;

public class AnnullamentoTests
{
    [Fact]
    public async Task Una_modifica_si_annulla_e_l_annullamento_resta_nello_storico()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var c = await commesse.CreaAsync("Restauro");
        c.Responsabile = "DGS";
        c.ValoreContrattuale = 1000.50m;
        c = await commesse.AggiornaAsync(c);

        var storico = amb.Get<StoricoService>();
        var modifica = (await storico.PerCommessaAsync(c.Id)).First(v => v.Operazione == "Modifica");
        var anteprima = await amb.Get<AnnullamentoService>().AnteprimaAsync(modifica.Id);
        Assert.True(anteprima.Possibile);
        Assert.False(anteprima.RichiedeConfermaAggiuntiva);

        await amb.Get<AnnullamentoService>().AnnullaAsync(modifica.Id, confermaSovrascrittura: false);

        var dopo = (await commesse.OttieniAsync(c.Id))!;
        Assert.Null(dopo.Responsabile);
        Assert.Null(dopo.ValoreContrattuale);
        var ultima = (await storico.PerCommessaAsync(c.Id)).First();
        Assert.StartsWith("Annullamento della modifica", ultima.Nota);
    }

    [Fact]
    public async Task Se_il_campo_e_cambiato_dopo_serve_la_conferma()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commesse = amb.Get<CommesseService>();
        var c = await commesse.CreaAsync("Restauro");
        c.Responsabile = "DGS";
        c = await commesse.AggiornaAsync(c);
        var prima = (await amb.Get<StoricoService>().PerCommessaAsync(c.Id)).First(v => v.Operazione == "Modifica");
        c.Responsabile = "MR";
        c = await commesse.AggiornaAsync(c);

        var annulla = amb.Get<AnnullamentoService>();
        var anteprima = await annulla.AnteprimaAsync(prima.Id);
        Assert.True(anteprima.RichiedeConfermaAggiuntiva);
        Assert.Equal(1, anteprima.ModificheSuccessive);
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => annulla.AnnullaAsync(prima.Id, confermaSovrascrittura: false));

        await annulla.AnnullaAsync(prima.Id, confermaSovrascrittura: true);
        Assert.Null((await commesse.OttieniAsync(c.Id))!.Responsabile);
    }

    [Fact]
    public async Task Gli_inserimenti_non_si_annullano()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var c = await amb.Get<CommesseService>().CreaAsync("Restauro");
        var inserimento = (await amb.Get<StoricoService>().PerCommessaAsync(c.Id)).Single();
        Assert.False(AnnullamentoService.Annullabile(inserimento));
        Assert.False((await amb.Get<AnnullamentoService>().AnteprimaAsync(inserimento.Id)).Possibile);
    }
}
