using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class AnnullamentoPreventivoTests
{
    [Fact]
    public async Task Le_modifiche_del_modulo_si_annullano_dallo_storico_del_Core()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.ImpostaRibassoAsync(id, (await prv.StatoAsync(id)).Versione, 21.5m);
        await prv.ImpostaRibassoAsync(id, (await prv.StatoAsync(id)).Versione, 18m);

        var storico = await amb.Get<StoricoService>().PerCommessaAsync(id);
        var ultima = storico.First(v => v.Operazione == "Modifica" && v.Entita == "Preventivo");
        await amb.Get<AnnullamentoService>().AnnullaAsync(ultima.Id, confermaSovrascrittura: false);

        Assert.Equal(21.5m, (await prv.StatoAsync(id)).RibassoPercentuale);
    }
}
