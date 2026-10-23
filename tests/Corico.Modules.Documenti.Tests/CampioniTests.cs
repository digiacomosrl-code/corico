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

public class CampioniMailTests
{
    [Fact]
    public void Mail_outlook_con_codifica_windows_e_immagini_della_firma_escluse()
    {
        if (CampioniTests.Campione("Offerte fornitori/Da fimas srl.msg") is not { } percorso) return;
        var (testo, dati) = LettoreMail.LeggiMsg(percorso);
        Assert.Contains("fimas@fimas.it", dati.Mittente);
        Assert.Contains("Monostrato Vulcanico", testo);
        // Logo e firma sono immagini in linea: resta solo il PDF scansionato dell'ordine.
        Assert.Equal("MX-2630N_20260302_180531.pdf", Assert.Single(dati.Allegati).Nome);
    }

    [Fact]
    public async Task Offerta_scansionata_letta_con_l_ocr()
    {
        if (CampioniTests.Campione("Offerte fornitori/edilzeta sol 2.pdf") is not { } percorso) return;
        var ocr = new OcrTesseract();
        if (!ocr.Disponibile) return;
        var esito = await new LetturaDocumento(ocr).LeggiAsync(percorso, ".pdf", null, default);
        Assert.All(esito.Pagine, p => Assert.True(p.DaOcr));
        Assert.Contains("22.374,00", esito.Pagine[0].Testo);
        Assert.Equal(Classificatore.Offerta, Classificatore.Proponi(esito.Pagine.Select(p => p.Testo).ToList(), mail: false)!.Tipo);
    }
}
