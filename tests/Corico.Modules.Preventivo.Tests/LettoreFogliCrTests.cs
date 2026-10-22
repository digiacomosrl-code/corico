using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Xunit.Abstractions;

namespace Corico.Modules.Preventivo.Tests;

public class LettoreFogliCrTests(ITestOutputHelper output)
{
    internal static string? Campione(string nome)
    {
        var cartella = Environment.GetEnvironmentVariable("CORICO_ESEMPI") ?? "/mnt/project-files/Esempi";
        var percorso = Path.Combine(cartella, nome);
        return File.Exists(percorso) ? percorso : null;
    }

    [Theory]
    [InlineData("24.03.03.001", "24.3.3.1")]
    [InlineData(" 26.01.13 ", "26.1.13")]
    [InlineData("NP INTO INT -", "NPINTOINT")]
    [InlineData("NP._1", "NP._1")]
    public void Chiave_tariffa(string tariffa, string attesa) => Assert.Equal(attesa, LettoreFogliCr.ChiaveTariffa(tariffa));

    [Theory]
    [InlineData("Previsione", StatoCosto.Previsione)]
    [InlineData("Previsionale", StatoCosto.Previsione)]
    [InlineData("Provvisorio", StatoCosto.Provvisorio)]
    [InlineData("STATO FINALE", StatoCosto.Definitivo)]
    [InlineData("Consuntivo al 1° SAL", StatoCosto.Definitivo)]
    public void Stato_del_costo_dal_foglio(string testo, StatoCosto atteso) => Assert.Equal(atteso, LettoreFogliCr.Stato(testo));

    [Fact]
    public void Campione_Caltagirone_fogli_e_tabellone()
    {
        if (Campione("1.Surly Caltagirone.xlsx") is not { } percorso) return;
        using var f = File.OpenRead(percorso);
        var letta = LettoreFogliCr.Leggi(f);

        foreach (var cr in letta.Fogli)
            output.WriteLine($"{cr.Nome} | {cr.StatoTesto} | {cr.Ribasso} | {cr.Descrizione} | {cr.Fornitore} | voci {cr.Righe.Count(r => r.Tipo == TipoRigaCr.Voce)} corpo {cr.Righe.Count(r => r.Tipo == TipoRigaCr.CostoACorpo)} | {cr.RicavoNettoFoglio} / {cr.CostoFoglio}");
        foreach (var h in letta.Home)
            output.WriteLine($"HOME {h.Riga} {h.Gruppo} | {h.Attivita} | {h.Oda} | {h.Fornitore} | {h.FoglioCollegato} | {h.Ricavo} / {h.Costo} | {h.Note} {h.Firma} {h.DataNota}");
        output.WriteLine("Ignorati: " + string.Join(", ", letta.FogliIgnorati));

        Assert.Equal("214", letta.CodiceCommessa);
        var telaio = letta.Fogli.Single(x => x.Nome == "CR_Ponteggio (Telaio)");
        Assert.Equal(0m, telaio.Ribasso);
        Assert.Equal("PONTEGGIO", telaio.Descrizione);
        Assert.Equal("Armilli Costruzioni srls", telaio.Fornitore);
        Assert.Equal(5, telaio.Righe.Count(r => r.Tipo == TipoRigaCr.Voce));
        Assert.Contains(letta.Home, h => h.Attivita == "Ponteggio" && h.FoglioCollegato == "CR_Ponteggio (Telaio)");
        Assert.Contains(letta.Home, h => h.Attivita == "Vetrate interne" && h.FoglioCollegato == "CR VetrateInterne");
        Assert.Contains(letta.Home, h => h.Attivita == "Piattaforma elevatrice" && h.FoglioCollegato == "CR_Piattaforma_elev");
        Assert.Contains("CR Model", letta.FogliIgnorati);
    }
}
