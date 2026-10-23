using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

/// <summary>Da dove arriva il nuovo prezzo proposto nella pagina «Aggiorna prezzi».</summary>
public enum FontePrezzo
{
    /// <summary>Ordine emesso a prezzo diverso dal pianificato: già applicato, compare solo per memoria.</summary>
    OrdineEmesso = 0,
    /// <summary>Analisi prezzi aziendale modificata dopo essere stata applicata alla voce.</summary>
    Analisi = 1,
    /// <summary>Nuova revisione di un'offerta già scelta.</summary>
    Revisione = 2,
    /// <summary>Variazione percentuale per categoria di costo.</summary>
    Variazione = 3,
}

/// <summary>Elemento di costo modificato da un aggiornamento.</summary>
public enum ElementoPrezzo
{
    /// <summary>Prezzo di una risorsa del livello analitico.</summary>
    Risorsa = 0,
    /// <summary>Una colonna del livello sintetico della voce d'offerta.</summary>
    Sintetico = 1,
    /// <summary>Prezzo di listino di una riga dell'offerta.</summary>
    RigaOfferta = 2,
    /// <summary>Importo di una voce a corpo.</summary>
    ACorpo = 3,
    /// <summary>Risorse di una voce copiate di nuovo da un'analisi aziendale.</summary>
    RisorseAnalisi = 4,
    /// <summary>Offerta di riferimento del pacchetto.</summary>
    Scelta = 5,
}

/// <summary>
/// Aggiornamento dei prezzi di costo confermato dalla pagina «Aggiorna prezzi» (rilascio 2B): operazione rilevante, annullabile
/// in blocco finché le stesse righe non sono state modificate di nuovo. Non tocca mai i prezzi contrattuali né le versioni.
/// </summary>
public sealed class AggiornamentoPrezzi : EntitaBase
{
    public Guid CommessaId { get; set; }
    public DateTime Data { get; set; }
    public string? Nota { get; set; }
    public int NumeroRighe { get; set; }
    public decimal DifferenzaCosto { get; set; }
    public decimal MdcPrima { get; set; }
    public decimal MdcDopo { get; set; }

    public DateTime? AnnullatoIl { get; set; }
    public string? AnnullatoDa { get; set; }

    public List<RigaAggiornamento> Righe { get; } = [];
}

public sealed class RigaAggiornamento : EntitaBase
{
    public Guid AggiornamentoId { get; set; }
    public Guid CommessaId { get; set; }
    public FontePrezzo Fonte { get; set; }
    public Guid PacchettoId { get; set; }
    public string Descrizione { get; set; } = "";
    public ElementoPrezzo Elemento { get; set; }
    public Guid ElementoId { get; set; }

    /// <summary>Colonna del livello sintetico (solo per <see cref="ElementoPrezzo.Sintetico"/>).</summary>
    public string? Campo { get; set; }

    public decimal PrezzoPrima { get; set; }
    public decimal PrezzoDopo { get; set; }
    public decimal DifferenzaCosto { get; set; }

    /// <summary>Stato dell'elemento prima e dopo, per l'annullamento (valore o elenco di risorse in JSON).</summary>
    public string Prima { get; set; } = "";
    public string Dopo { get; set; } = "";
}
