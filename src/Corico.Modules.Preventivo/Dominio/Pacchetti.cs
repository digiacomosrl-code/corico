using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

// Pacchetti di affidamento e offerte (prompt sez. 14.1, rilascio 1B).
// Le relazioni verso il Core (fornitori, documenti) sono per identificativo, senza chiavi esterne.

public enum TipoAffidamento
{
    Fornitura = 0,
    FornituraEPosa = 1,
    Subappalto = 2,
    NoloACaldo = 3,
    LavoriInEconomia = 4,
    EsecuzioneDiretta = 5,
}

/// <summary>Stato del costo del pacchetto (decisione 4 del 22/10/2026). Il Consuntivo arriverà con il Modulo 4.</summary>
public enum StatoCosto
{
    Previsione = 0,
    Provvisorio = 1,
    Definitivo = 2,
}

public enum TipoOfferta
{
    StimaInterna = 0,
    SolaFornitura = 1,
    FornituraEPosa = 2,
    SolaPosa = 3,
    Nolo = 4,
}

/// <summary>Gruppo di voci del computo affidato a uno stesso fornitore, subappaltatore o all'impresa (fogli «CR»).</summary>
public sealed class Pacchetto : EntitaBase
{
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }

    /// <summary>Codice di riferimento (colonna «Gruppo» del tabellone, es. 26.01.04.002).</summary>
    public string? Codice { get; set; }
    public string Descrizione { get; set; } = "";
    public TipoAffidamento Tipo { get; set; }

    /// <summary>Riferimento d'ordine o d'affidamento (colonna «ODA» del tabellone).</summary>
    public string? RiferimentoOda { get; set; }

    public StatoCosto StatoCosto { get; set; }

    /// <summary>Offerta di riferimento, che alimenta costo e MDC del pacchetto.</summary>
    public Guid? OffertaSceltaId { get; set; }

    public string? Note { get; set; }
    public bool Archiviato { get; set; }

    public List<PacchettoVoce> Voci { get; } = [];
}

/// <summary>Assegnazione di una voce del computo a un pacchetto: una voce appartiene a un solo pacchetto.</summary>
public sealed class PacchettoVoce : EntitaBase
{
    public Guid PacchettoId { get; set; }
    public Guid CommessaId { get; set; }
    public Guid VoceComputoId { get; set; }
}

/// <summary>Nota datata con le iniziali dell'autore (sostituisce le colonne Note/Firma/Data del foglio HOME).</summary>
public sealed class NotaPacchetto : EntitaBase
{
    public Guid PacchettoId { get; set; }
    public Guid CommessaId { get; set; }
    public DateOnly Data { get; set; }
    public string? Iniziali { get; set; }
    public string Testo { get; set; } = "";
}

/// <summary>Offerta di un fornitore (o stima interna) per un pacchetto. Più offerte alternative per pacchetto.</summary>
public sealed class Offerta : EntitaBase
{
    public Guid PacchettoId { get; set; }
    public Guid CommessaId { get; set; }

    public Guid? FornitoreId { get; set; }

    /// <summary>Nome del fornitore come indicato nell'offerta o nel foglio importato.</summary>
    public string? NomeFornitore { get; set; }

    public TipoOfferta Tipo { get; set; }
    public string? Numero { get; set; }
    public string? Revisione { get; set; }
    public DateOnly? Data { get; set; }
    public int? ValiditaGiorni { get; set; }
    public DateOnly? Scadenza { get; set; }
    public string? Pagamento { get; set; }
    public string? Consegna { get; set; }
    public string? Garanzia { get; set; }
    public Guid? DocumentoId { get; set; }

    /// <summary>Esclusioni e oneri a carico dell'impresa: evidenziati nel confronto.</summary>
    public string? Esclusioni { get; set; }

    /// <summary>Totale imponibile riportato sull'offerta, da confrontare con il totale calcolato.</summary>
    public decimal? TotaleDichiarato { get; set; }

    public string? Note { get; set; }

    /// <summary>Foglio Excel da cui l'offerta è stata importata, se presente.</summary>
    public string? Origine { get; set; }

    public List<OffertaVoce> Voci { get; } = [];
    public List<RigaOfferta> Righe { get; } = [];
    public List<CostoACorpo> CostiACorpo { get; } = [];
}

/// <summary>
/// Costo di una voce del pacchetto secondo un'offerta, a livello sintetico (sez. 13.1): costo unitario in tre colonne.
/// La quantità di pianificazione, se indicata, sostituisce quella del computo per ricavo e costo (decisione 2: nota obbligatoria).
/// </summary>
public sealed class OffertaVoce : EntitaBase
{
    public Guid OffertaId { get; set; }
    public Guid CommessaId { get; set; }
    public Guid VoceComputoId { get; set; }

    public decimal? QuantitaPianificazione { get; set; }
    public string? NotaQuantita { get; set; }

    public decimal Fornitura { get; set; }
    public decimal FornituraEPosa { get; set; }
    public decimal ManodoperaENoli { get; set; }
}

/// <summary>Riga dell'offerta del fornitore, assegnabile a una voce del computo (più righe per la stessa voce).</summary>
public sealed class RigaOfferta : EntitaBase
{
    public Guid OffertaId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }

    public string? CodiceArticolo { get; set; }
    public string Descrizione { get; set; } = "";
    public string? Posizione { get; set; }
    public string? Unita { get; set; }
    public decimal Quantita { get; set; }
    public decimal PrezzoListino { get; set; }
    public decimal ScontoPercentuale { get; set; }

    public Guid? VoceComputoId { get; set; }
}

/// <summary>Voce a corpo dell'offerta (trasporto, posa, imballo…) o costo aggiuntivo del pacchetto non legato a voci.</summary>
public sealed class CostoACorpo : EntitaBase
{
    public Guid OffertaId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string Descrizione { get; set; } = "";

    /// <summary>Importo; se l'offerta lo dichiara «incluso» l'importo è zero e <see cref="Incluso"/> è vero.</summary>
    public decimal Importo { get; set; }
    public bool Incluso { get; set; }
}
