using Corico.Core.Servizi;

namespace Corico.Core.Tests;

public class RicercaTestoTests
{
    [Theory]
    [InlineData("Comune di Caltagirone", "caltag", true)]
    [InlineData("Comune di Caltagirone", "caltagirone comune", true)]   // parole in qualsiasi ordine
    [InlineData("Comune di Caltagirone", "comune ragusa", false)]
    [InlineData("CALCESTRUZZI DEL SUD S.R.L.", "srl sud", true)]        // punti ignorati
    [InlineData("FERRAMENTA ÀNGELO", "angelo", true)]                   // accenti ignorati
    [InlineData("Impresa dell'Industria", "industria", true)]
    [InlineData("Bianchi Paolo 01987654321 · Ragusa", "0198765", true)] // anche sul dettaglio (P.IVA, comune)
    [InlineData("Qualsiasi", "", true)]
    public void Corrisponde_per_parole(string testo, string ricerca, bool atteso) =>
        Assert.Equal(atteso, RicercaTesto.Corrisponde(testo, RicercaTesto.Parole(ricerca)));
}
