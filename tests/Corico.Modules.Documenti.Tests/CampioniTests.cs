using Corico.Modules.Documenti.Estrazione;
using Corico.Modules.Documenti.Servizi;

namespace Corico.Modules.Documenti.Tests;

/// <summary>File campione della cartella Esempi (non nel repository): le prove si saltano se mancano.</summary>
public class CampioniTests
{
    internal static string? Campione(string nome)
    {
        var cartella = Environment.GetEnvironmentVariable("CORICO_ESEMPI") ?? "/mnt/project-files/Esempi";
        var percorso = Path.Combine(cartella, nome);
        return File.Exists(percorso) ? percorso : null;
    }

    [Theory]
    [InlineData("Offerte fornitori/Offerta Padilla.pdf", Classificatore.Offerta, "PORTA REI60")]
    [InlineData("Offerte fornitori/PR_3000 r00.pdf", Classificatore.Offerta, "Aura 100")]
    [InlineData("SF_Biancavilla.pdf", Classificatore.Sal, "Biancavilla")]
    [InlineData("cme perizia di assestamento.pdf", Classificatore.Computo, "Sgombero")]
    public void Pdf_campione_con_testo(string nome, string tipo, string contiene)
    {
        if (Campione(nome) is not { } percorso) return;
        var pagine = LettorePdf.Leggi(percorso);
        Assert.All(pagine.Take(3), p => Assert.False(LettorePdf.SenzaTesto(p.Testo)));
        Assert.Contains(contiene, string.Join("\n", pagine.Select(p => p.Testo)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(tipo, Classificatore.Proponi(pagine.Select(p => p.Testo).ToList(), mail: false)!.Tipo);
    }

    [Fact]
    public void Perizia_di_variante_e_una_scansione_senza_testo()
    {
        if (Campione("CM Perizia di variante.pdf") is not { } percorso) return;
        var pagine = LettorePdf.Leggi(percorso);
        Assert.Equal(183, pagine.Count);
        Assert.All(pagine, p => Assert.True(LettorePdf.SenzaTesto(p.Testo)));
    }
}
