using Corico.Modules.Documenti.Servizi;

namespace Corico.Modules.Documenti.Tests;

public class RegoleTests
{
    [Theory]
    // Intestazioni come nei file campione (SF_Biancavilla, cme perizia di assestamento, Offerta Padilla, PR_3000).
    [InlineData("ASP di Catania\nLAVORI : PNRR\nSTATO FINALE DEI LAVORI\nImporto del progetto originario", Classificatore.Sal)]
    [InlineData("Comune di Biancavilla\nCOMPUTO METRICO ESTIMATIVO\nPERIZIA DI ASSESTAMENTO", Classificatore.Computo)]
    [InlineData("PADILLA S.R.L.\nPreventivo cliente n. 260561 del 12/02/2026\nDescrizione Quantità Prezzo", Classificatore.Offerta)]
    [InlineData("OFFERTA ECONOMICA\nPreventivo: PR_3000 r.00\ncome da computo metrico allegato", Classificatore.Offerta)]
    [InlineData("LISTINO PREZZI 2026\nArticolo Descrizione Prezzo", Classificatore.Listino)]
    [InlineData("ORDINE DI ACQUISTO n. 2026/001\nFornitore", Classificatore.Ordine)]
    public void Tipo_proposto_dalla_parola_che_compare_per_prima(string testo, string atteso) =>
        Assert.Equal(atteso, Classificatore.Proponi([testo], mail: false)!.Tipo);

    [Fact]
    public void Nessuna_proposta_senza_parole_chiave_e_le_mail_sono_sempre_corrispondenza()
    {
        Assert.Null(Classificatore.Proponi(["Relazione geologica"], mail: false));
        Assert.Equal(Classificatore.Corrispondenza, Classificatore.Proponi(["Buongiorno, in allegato la nostra offerta"], mail: true)!.Tipo);
    }

    [Fact]
    public void Firma_uguale_per_impaginazione_diversa_e_bassa_per_testi_diversi()
    {
        var a = DocumentiDiProva.TestoOfferta("Padilla", 1);
        var fa = Somiglianza.Firma(a)!;
        Assert.Equal(1.0, Somiglianza.Confronta(fa, Somiglianza.Firma(a.ToUpperInvariant().Replace("\n", "\n\n  "))!));
        Assert.True(Somiglianza.Confronta(fa, Somiglianza.Firma(DocumentiDiProva.TestoOfferta("Altro", 2))!) < 0.3);
        Assert.Null(Somiglianza.Firma("testo troppo breve per un confronto"));
    }

    [Theory]
    [InlineData("porta REI60", "\"porta\" \"rei60\"*")]
    [InlineData("  Qualità\"  ", "\"qualita\"*")]
    [InlineData("N.20 Deposito", "\"n\" \"20\" \"deposito\"*")]
    [InlineData("*()", null)]
    public void Espressione_di_ricerca_solo_parole_tra_virgolette(string testo, string? attesa) =>
        Assert.Equal(attesa, IndiceRicerca.Espressione(testo));
}
