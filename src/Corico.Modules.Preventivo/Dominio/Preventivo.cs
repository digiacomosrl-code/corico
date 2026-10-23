using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

/// <summary>Preventivo in bozza della commessa: uno per commessa.</summary>
public sealed class Preventivo : EntitaBase, IDatoTecnico
{
    public Guid CommessaId { get; set; }

    /// <summary>Ribasso d'asta unico di commessa, in percentuale (es. 21,5). Decisione del 22/10/2026.</summary>
    public decimal RibassoPercentuale { get; set; }

    /// <summary>Obiettivo di margine (MDC sui ricavi, in percentuale); null se non fissato.</summary>
    public decimal? ObiettivoMarginePercentuale { get; set; }

    public string? Note { get; set; }
}

public enum OrigineComputo
{
    Manuale = 0,
    Xpwe = 1,
    Excel = 2,
    Pdf = 3,
}

/// <summary>
/// Computo metrico della commessa. Una revisione o perizia crea un nuovo computo;
/// solo uno è attivo e alimenta i ricavi.
/// </summary>
public sealed class Computo : EntitaBase
{
    public Guid PreventivoId { get; set; }
    public Guid CommessaId { get; set; }
    public string Descrizione { get; set; } = "";
    public OrigineComputo Origine { get; set; }
    public bool Attivo { get; set; }

    public Guid? DocumentoId { get; set; }
    public string? NomeFile { get; set; }

    /// <summary>Totale calcolato al momento dell'importazione, per il confronto con il documento.</summary>
    public decimal? TotaleImportato { get; set; }

    /// <summary>Totale riportato sul documento originale (stampa del computo), indicato dall'utente.</summary>
    public decimal? TotaleDichiarato { get; set; }

    public string? Oggetto { get; set; }
    public string? Comune { get; set; }
    public string? Committente { get; set; }
    public string? Impresa { get; set; }
}

public enum LivelloCategoria
{
    SuperCategoria = 0,
    Categoria = 1,
    SubCategoria = 2,
    SuperCapitolo = 3,
    Capitolo = 4,
    SubCapitolo = 5,
}

public sealed class Categoria : EntitaBase
{
    public Guid ComputoId { get; set; }
    public Guid CommessaId { get; set; }
    public LivelloCategoria Livello { get; set; }
    public string IdOriginale { get; set; } = "";
    public string? Codice { get; set; }
    public string Descrizione { get; set; } = "";
}

/// <summary>Voce dell'elenco prezzi del computo.</summary>
public sealed class VoceElencoPrezzi : EntitaBase
{
    public Guid ComputoId { get; set; }
    public Guid CommessaId { get; set; }
    public string? IdOriginale { get; set; }
    public string Tariffa { get; set; } = "";
    public string? Articolo { get; set; }
    public string? DescrizioneRidotta { get; set; }
    public string? DescrizioneBreve { get; set; }
    public string? DescrizioneEstesa { get; set; }

    /// <summary>Unità come scritta nel documento originale.</summary>
    public string? UnitaOriginale { get; set; }

    /// <summary>Unità normalizzata (es. "cad.", "cadauno" → "cad").</summary>
    public string? Unita { get; set; }

    public decimal Prezzo { get; set; }
    public decimal? IncidenzaSicurezza { get; set; }
    public decimal? IncidenzaManodopera { get; set; }
    public decimal? IncidenzaMateriali { get; set; }
    public decimal? IncidenzaAttrezzature { get; set; }

    public Guid? SuperCapitoloId { get; set; }

    public bool HaAnalisi { get; set; }
    public decimal? SpeseGeneraliAnalisi { get; set; }
    public decimal? UtileAnalisi { get; set; }

    public List<RigaAnalisi> RigheAnalisi { get; set; } = [];
}

/// <summary>Elemento di un'analisi prezzi incorporata nel computo.</summary>
public sealed class RigaAnalisi : EntitaBase
{
    public Guid VoceElencoPrezziId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public int Tipo { get; set; }

    /// <summary>Voce dell'elenco prezzi richiamata, se presente.</summary>
    public Guid? ElencoPrezziRiferitoId { get; set; }

    public string? Descrizione { get; set; }
    public string? Unita { get; set; }
    public decimal Quantita { get; set; }
    public decimal Prezzo { get; set; }
}

/// <summary>Voce del computo metrico.</summary>
public sealed class VoceComputo : EntitaBase
{
    public Guid ComputoId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string? IdOriginale { get; set; }

    /// <summary>Voce di elenco prezzi collegata; null se il documento non ne indica una valida.</summary>
    public Guid? VoceElencoPrezziId { get; set; }
    public VoceElencoPrezzi? VoceElencoPrezzi { get; set; }

    /// <summary>Dati copiati dall'elenco prezzi all'importazione (o inseriti a mano).</summary>
    public string? Tariffa { get; set; }
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }
    public decimal Quantita { get; set; }
    public decimal Prezzo { get; set; }

    /// <summary>
    /// Quota del prezzo non soggetta a ribasso, in percentuale (100 = oneri di sicurezza;
    /// 0 = interamente soggetta). Proposta dall'incidenza di sicurezza importata.
    /// </summary>
    public decimal PercentualeNonSoggetta { get; set; }

    public Guid? SuperCategoriaId { get; set; }
    public Guid? CategoriaId { get; set; }
    public Guid? SubCategoriaId { get; set; }
    public string? CodiceWbs { get; set; }

    public string? Note { get; set; }
    public bool Archiviata { get; set; }

    public List<RigaMisura> RigheMisura { get; set; } = [];
}

public sealed class RigaMisura : EntitaBase
{
    public Guid VoceComputoId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string? Descrizione { get; set; }
    public decimal? PartiUguali { get; set; }
    public decimal? Lunghezza { get; set; }
    public decimal? Larghezza { get; set; }
    public decimal? AltezzaPeso { get; set; }
    public decimal? Quantita { get; set; }

    /// <summary>Riga che richiama la quantità di un'altra voce ("vedi voce").</summary>
    public string? VoceRiferita { get; set; }
}
