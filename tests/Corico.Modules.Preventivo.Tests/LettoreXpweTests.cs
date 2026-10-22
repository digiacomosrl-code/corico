using Corico.Modules.Preventivo.Importazione;
using Corico.Modules.Preventivo.Servizi;

namespace Corico.Modules.Preventivo.Tests;

public class LettoreXpweTests
{
    [Fact]
    public void Legge_dati_generali_elenco_prezzi_voci_e_misure()
    {
        var letto = LettoreXpwe.Leggi(XpweDiProva.Flusso());

        Assert.Equal("Lavori di prova", letto.Oggetto);
        Assert.Equal("Caltagirone", letto.Comune);
        Assert.Equal(1, letto.CaratteriRimossi);
        Assert.Equal(2, letto.ElencoPrezzi.Count);
        Assert.Equal(3, letto.Voci.Count);
        Assert.Equal(2, letto.Voci[0].Misure.Count);
        Assert.Equal(120.5m, letto.Voci[0].Quantita);
        Assert.Null(letto.Voci[2].IdElenco);
        Assert.Contains(letto.Categorie, c => c.Livello == "SuperCategoria" && c.Descrizione == "SICUREZZA");
    }

    [Fact]
    public void Uniforma_le_unita_di_misura_e_le_segnala()
    {
        var letto = LettoreXpwe.Leggi(XpweDiProva.Flusso());

        Assert.Equal("mq", letto.Elenco("10")!.Unita);
        Assert.Equal("cad", letto.Elenco("11")!.Unita);
        Assert.Equal("cadauno", letto.Elenco("11")!.UnitaOriginale);
        Assert.Contains(letto.Segnalazioni, s => s.Testo.StartsWith("Unità di misura uniformate"));
        Assert.Contains(letto.Segnalazioni, s => s.Gravita == GravitaSegnalazione.Attenzione && s.Testo.Contains("non sono collegate"));
    }

    [Fact]
    public void Anteprima_calcola_totale_e_quota_di_sicurezza()
    {
        var a = ImportazioneComputoService.Anteprima(XpweDiProva.Flusso());

        Assert.Equal(XpweDiProva.TotaleAtteso, a.TotaleCalcolato);
        Assert.Equal(2000m, a.TotaleSicurezza);
        Assert.Equal(1, a.VociSenzaElenco);
    }

    [Fact]
    public void Un_file_non_xpwe_viene_rifiutato_con_un_messaggio_chiaro()
    {
        using var ms = new MemoryStream("<Altro/>"u8.ToArray());
        var ex = Assert.Throws<FormatException>(() => LettoreXpwe.Leggi(ms));
        Assert.Contains("PweDocumento", ex.Message);
    }

    [Theory]
    [InlineData("m2", "mq")]
    [InlineData("MQ", "mq")]
    [InlineData("cad.", "cad")]
    [InlineData("cd", "cad")]
    [InlineData("ora", "h")]
    [InlineData("ac", "a corpo")]
    [InlineData("  ", null)]
    public void Normalizzazione_unita(string? originale, string? attesa)
    {
        Assert.Equal(attesa, UnitaMisura.Normalizza(originale));
    }

    /// <summary>
    /// Campione reale fornito da Salvatore. Il file contiene dati aziendali e non è nel repository:
    /// il test si esegue solo dove il file è presente (variabile CORICO_ESEMPI o cartella di progetto).
    /// </summary>
    [Fact]
    public void Campione_Biancavilla_totale_identico_a_PriMus()
    {
        var cartella = Environment.GetEnvironmentVariable("CORICO_ESEMPI") ?? "/mnt/project-files/Esempi";
        var percorso = Path.Combine(cartella, "BIANCAVILLA.XPWE");
        if (!File.Exists(percorso)) return;

        using var f = File.OpenRead(percorso);
        var a = ImportazioneComputoService.Anteprima(f);

        Assert.Equal(1_354_438.26m, Math.Round(a.TotaleCalcolato, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(297, a.Letto.Voci.Count);
        Assert.Equal(418, a.Letto.ElencoPrezzi.Count);
        Assert.Equal(22, a.VociSenzaElenco);
        Assert.True(a.TotaleSicurezza > 0);
    }
}
