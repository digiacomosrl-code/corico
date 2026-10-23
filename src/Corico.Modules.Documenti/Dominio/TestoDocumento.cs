using Corico.Core.Dominio;

namespace Corico.Modules.Documenti.Dominio;

public enum EsitoLettura { Completata, Parziale, NonSupportata, Errore }

public enum MetodoLettura { Testo, Ocr, Misto, Nessuno }

/// <summary>
/// Testo estratto da un documento dell'archivio del Core (collegato per identificativo).
/// Si crea una sola volta, a lettura conclusa: i documenti senza questo record sono «in coda».
/// </summary>
public sealed class TestoDocumento : EntitaBase, IDatoTecnico
{
    public Guid DocumentoId { get; set; }
    public EsitoLettura Esito { get; set; }
    public MetodoLettura Metodo { get; set; }
    public int NumeroPagine { get; set; }
    public int PagineOcr { get; set; }

    /// <summary>Spiegazione per l'utente in caso di lettura parziale, non supportata o non riuscita.</summary>
    public string? Messaggio { get; set; }

    /// <summary>Tipo di documento proposto dalle regole sul testo (sez. 4 della specifica); null se nessuna regola si applica.</summary>
    public string? TipoProposto { get; set; }
    public string? MotivoTipo { get; set; }

    /// <summary>True quando un utente ha confermato o scartato la proposta: non viene più mostrata.</summary>
    public bool TipoEsaminato { get; set; }

    /// <summary>Firma del contenuto per i duplicati simili (MinHash in esadecimale); null se il testo è troppo breve.</summary>
    public string? Firma { get; set; }

    // Dati della mail, se il documento è un file .eml o .msg.
    public string? MailMittente { get; set; }
    public string? MailOggetto { get; set; }
    public DateTime? MailData { get; set; }

    public List<PaginaTesto> Pagine { get; set; } = [];
}

/// <summary>Testo di una pagina (per Word e mail il documento intero è una sola pagina, per Excel ogni foglio).</summary>
public sealed class PaginaTesto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestoDocumentoId { get; set; }
    public int Numero { get; set; }
    public string Testo { get; set; } = "";
    public bool DaOcr { get; set; }
}

/// <summary>Allegato di una mail, salvato nell'archivio come documento a sé e collegato alla mail.</summary>
public sealed class AllegatoMail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MailDocumentoId { get; set; }
    public Guid AllegatoDocumentoId { get; set; }
    public string NomeFile { get; set; } = "";
}
