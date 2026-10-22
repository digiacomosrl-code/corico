using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

/// <summary>Le sei categorie delle risorse e dei costi diretti (sez. 13.2).</summary>
public enum CategoriaRisorsa
{
    Materiali = 1,
    Manodopera = 2,
    Attrezzature = 3,
    Subappalti = 4,
    TrasportiESmaltimenti = 5,
    AltriCosti = 6,
}

/// <summary>Campi comuni a una risorsa: della voce di un'offerta (livello analitico) o di un'analisi aziendale.</summary>
public interface IRisorsa
{
    int Ordine { get; set; }
    CategoriaRisorsa Categoria { get; set; }
    string Descrizione { get; set; }
    string? Unita { get; set; }
    decimal Fabbisogno { get; set; }
    decimal SfridoPercentuale { get; set; }
    decimal Prezzo { get; set; }
    string? Fonte { get; set; }
    string? Note { get; set; }
}

public static class Risorse
{
    /// <summary>Costo della risorsa per unità della lavorazione: fabbisogno × (1 + sfrido) × prezzo.</summary>
    public static decimal CostoUnitario(IRisorsa r) => r.Fabbisogno * (1m + r.SfridoPercentuale / 100m) * r.Prezzo;

    /// <summary>Quantità della risorsa per unità della lavorazione, compreso lo sfrido.</summary>
    public static decimal FabbisognoEffettivo(IRisorsa r) => r.Fabbisogno * (1m + r.SfridoPercentuale / 100m);
}

/// <summary>
/// Risorsa necessaria a una voce secondo un'offerta (livello analitico, sez. 13.2).
/// Se una voce ha risorse, il suo costo unitario è la loro somma e sostituisce le tre colonne del livello sintetico.
/// </summary>
public sealed class RisorsaVoce : EntitaBase, IRisorsa
{
    public Guid OffertaVoceId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public CategoriaRisorsa Categoria { get; set; } = CategoriaRisorsa.Materiali;
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }

    /// <summary>Quantità di risorsa per unità della voce (es. 0,8 h di manodopera per m²).</summary>
    public decimal Fabbisogno { get; set; }

    public decimal SfridoPercentuale { get; set; }
    public decimal Prezzo { get; set; }
    public string? Fonte { get; set; }
    public string? Note { get; set; }

    /// <summary>Analisi aziendale da cui la risorsa è stata copiata, se presente.</summary>
    public Guid? AnalisiId { get; set; }
}

public enum StatoAnalisi
{
    Bozza = 0,
    Confermata = 1,
    Archiviata = 2,
}

/// <summary>
/// Analisi prezzi aziendale riutilizzabile (sez. 15): lavorazione con le risorse per unità.
/// Applicarla a una voce ne copia le risorse: le modifiche successive non toccano i preventivi già fatti.
/// </summary>
public sealed class AnalisiAziendale : EntitaBase
{
    public string? Codice { get; set; }
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }
    public StatoAnalisi Stato { get; set; }
    public string? Fonte { get; set; }
    public string? Note { get; set; }
    public List<RigaAnalisiAziendale> Righe { get; } = [];
}

public sealed class RigaAnalisiAziendale : EntitaBase, IRisorsa
{
    public Guid AnalisiId { get; set; }
    public int Ordine { get; set; }
    public CategoriaRisorsa Categoria { get; set; } = CategoriaRisorsa.Materiali;
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }
    public decimal Fabbisogno { get; set; }
    public decimal SfridoPercentuale { get; set; }
    public decimal Prezzo { get; set; }
    public string? Fonte { get; set; }
    public string? Note { get; set; }
}
