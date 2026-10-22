using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class CalcoloRicaviTests
{
    [Fact]
    public void Prezzo_ribassato_come_nei_fogli_CR_senza_arrotondamenti()
    {
        // Foglio CR Caltagirone: K = (1 − 0,215) × I, L = H × K.
        var r = CalcoloRicavi.Voce(quantita: 12.5m, prezzo: 697.67m, ribassoPercentuale: 21.5m, percentualeNonSoggetta: 0);

        Assert.Equal(697.67m * 0.785m, r.PrezzoRibassato);
        Assert.Equal(12.5m * 697.67m * 0.785m, r.Netto);
        Assert.Equal(12.5m * 697.67m, r.Lordo);
        Assert.Equal(0m, r.LordoNonSoggetto);
    }

    [Fact]
    public void Le_voci_non_soggette_non_subiscono_il_ribasso()
    {
        var sicurezza = CalcoloRicavi.Voce(10m, 100m, 21.5m, 100m);
        Assert.Equal(1000m, sicurezza.Netto);
        Assert.Equal(1000m, sicurezza.LordoNonSoggetto);

        // Quota parziale: il 30% del prezzo è escluso dal ribasso.
        var parziale = CalcoloRicavi.Voce(1m, 100m, 20m, 30m);
        Assert.Equal(100m * (1m - 0.20m * 0.70m), parziale.Netto);
        Assert.Equal(30m, parziale.LordoNonSoggetto);
    }

    [Fact]
    public void Quadro_economico_somma_le_voci_e_segnala_la_differenza_dal_contratto()
    {
        var q = QuadroEconomico.Calcola(20m,
            [(10m, 100m, 0m), (1m, 500m, 100m), (3m, 0m, 0m)], valoreContrattuale: 1400m);

        Assert.Equal(1500m, q.ImportoLavoriLordo);
        Assert.Equal(500m, q.ImportoNonSoggetto);
        Assert.Equal(1000m, q.ImportoSoggetto);
        Assert.Equal(200m, q.ImportoRibasso);
        Assert.Equal(1300m, q.ImportoNetto);
        Assert.Equal(100m, q.DifferenzaDaContratto);
        Assert.Equal(3, q.NumeroVoci);
        Assert.Equal(1, q.VociSenzaPrezzo);
    }

    [Fact]
    public void Senza_valore_contrattuale_la_differenza_non_e_calcolata()
    {
        Assert.Null(QuadroEconomico.Calcola(0m, [], null).DifferenzaDaContratto);
    }
}
