using Corico.Core.Calcolo;

namespace Corico.Core.Tests;

public class ImportiTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("2.344999", "2.34")]
    [InlineData("10", "10")]
    public void Arrotonda_come_Excel(string valore, string atteso) =>
        Assert.Equal(decimal.Parse(atteso, System.Globalization.CultureInfo.InvariantCulture),
            Importi.Arrotonda(decimal.Parse(valore, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Margine_e_ricarico_calcolati_separatamente()
    {
        // Ricavo 100, costo 80: margine 20% sul ricavo, ricarico 25% sul costo.
        Assert.Equal(20m, Importi.MargineSulRicavo(100m, 80m));
        Assert.Equal(25m, Importi.RicaricoSulCosto(100m, 80m));
    }

    [Fact]
    public void Divisione_per_zero_o_valori_mancanti_danno_nd()
    {
        Assert.Null(Importi.MargineSulRicavo(0m, 50m));
        Assert.Null(Importi.RicaricoSulCosto(100m, 0m));
        Assert.Null(Importi.MargineSulRicavo(null, 50m));
        Assert.Equal("n.d.", Formato.Percento(Importi.MargineSulRicavo(0m, 50m)));
    }

    [Fact]
    public void Caso_reale_pacchetto_ponteggio_Caltagirone()
    {
        // HOME riga 12: ricavo 96.997,77 — costo 75.415,00 — MDC 21.582,77 — 22,25%.
        var ricavo = 96997.77m;
        var costo = 75415m;
        Assert.Equal(21582.77m, ricavo - costo);
        Assert.Equal(22.25m, Importi.Arrotonda(Importi.MargineSulRicavo(ricavo, costo)!.Value));
    }

    [Fact]
    public void Formato_italiano()
    {
        Assert.Equal("1.234.567,89 €", Formato.Euro(1234567.891m));
        Assert.Equal("22,25 %", Formato.Percento(22.2507m));
    }
}
