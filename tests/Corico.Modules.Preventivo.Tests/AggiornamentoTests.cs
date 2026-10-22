using Corico.Core.Aggiornamento;
using Corico.Core.Servizi;
using Corico.Core.Tests;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class AggiornamentoTests
{
    [Fact]
    public async Task I_controlli_dopo_l_aggiornamento_ricalcolano_commesse_e_versioni()
    {
        await using var amb = await AmbienteDiProva.CreaAsync(new PreventivoModulo());
        var id = (await amb.Get<CommesseService>().CreaAsync("Commessa di prova")).Id;
        var prv = amb.Get<PreventivoService>();
        await prv.SalvaVoceAsync(id, new DatiVoce { Tariffa = "A", Descrizione = "Scavo", Unita = "mc", Quantita = 10, Prezzo = 25 });
        await amb.Get<VersioniService>().PubblicaAsync(id, "Prima versione");

        var esiti = await ControlliAggiornamento.VerificaAsync(amb.Servizi, null, simulaErrore: false);
        Assert.All(esiti, e => Assert.True(e.Superato, e.Controllo + ": " + e.Dettaglio));
        Assert.Contains(esiti, e => e.Controllo == "Versioni ufficiali" && e.Dettaglio.StartsWith("versioni rilette: 1"));

        var conErrore = await ControlliAggiornamento.VerificaAsync(amb.Servizi, null, simulaErrore: true);
        Assert.Contains(conErrore, e => !e.Superato);

        var uscita = new StringWriter();
        Assert.Equal(0, await ControlliAggiornamento.EseguiAsync(amb.Servizi, ["copia-sicurezza"], uscita));
        var copia = uscita.ToString().Trim()["COPIA=".Length..];
        Assert.True(File.Exists(copia));
    }
}
