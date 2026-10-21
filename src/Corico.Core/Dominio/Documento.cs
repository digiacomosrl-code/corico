namespace Corico.Core.Dominio;

/// <summary>
/// Metadati di un documento dell'archivio. Il file è salvato con un nome interno
/// nella cartella Documenti; il nome originale è conservato qui.
/// </summary>
public sealed class Documento : EntitaBase
{
    public string NomeOriginale { get; set; } = "";
    public string Estensione { get; set; } = "";
    public string TipoContenuto { get; set; } = "application/octet-stream";

    /// <summary>Percorso relativo alla cartella Documenti.</summary>
    public string PercorsoInterno { get; set; } = "";

    public long Dimensione { get; set; }

    /// <summary>Impronta SHA-256 in esadecimale, per duplicati esatti e verifica di integrità.</summary>
    public string Impronta { get; set; } = "";

    public string? Tipo { get; set; }
    public DateOnly? DataDocumento { get; set; }
    public string? Descrizione { get; set; }

    public Guid? CommessaId { get; set; }
    public Commessa? Commessa { get; set; }

    public bool Archiviato { get; set; }
}
