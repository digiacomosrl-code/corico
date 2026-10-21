namespace Corico.Core.Dominio;

public enum StatoCommessa
{
    DaAvviare = 0,
    Attiva = 1,
    Conclusa = 2,
}

public sealed class Commessa : EntitaBase
{
    /// <summary>Codice visibile, progressivo e mai riutilizzato (es. 2026-001).</summary>
    public string Codice { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public StatoCommessa Stato { get; set; } = StatoCommessa.DaAvviare;

    /// <summary>Archiviazione: distinta dallo stato, reversibile e tracciata.</summary>
    public bool Archiviata { get; set; }

    public Guid? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    /// <summary>Codice attribuito dal cliente o dal gestionale (es. 214).</summary>
    public string? CodiceEsterno { get; set; }

    public string? IndirizzoCantiere { get; set; }
    public string? ComuneCantiere { get; set; }
    public string? ProvinciaCantiere { get; set; }
    public string? Responsabile { get; set; }
    public DateOnly? DataInizioPrevista { get; set; }
    public DateOnly? DataFinePrevista { get; set; }

    /// <summary>Importo riepilogativo: non viene mai sommato ai ricavi analitici.</summary>
    public decimal? ValoreContrattuale { get; set; }

    public Guid? ContrattoDocumentoId { get; set; }
    public string? ContrattoNumero { get; set; }
    public DateOnly? ContrattoData { get; set; }

    public string? Note { get; set; }
}
