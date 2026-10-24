using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

// Qualificazione interna dei fornitori per commessa e comunicazione dell'esito a DL, RUP e CSE (richiesta del 24/10/2026).
// Sostituisce, in questo processo, la comunicazione alla Prefettura. Le relazioni verso il Core (fornitori, attori,
// documenti) sono per identificativo, senza chiavi esterne.

public enum TipoOperatore
{
    Fornitore = 0,
    ImpresaEsecutrice = 1,
    Subappaltatore = 2,
    SubAffidatario = 3,
}

public enum StatoQualifica
{
    /// <summary>Verifica documentale in corso: la scheda si modifica liberamente.</summary>
    InIstruttoria = 0,
    /// <summary>Esito positivo approvato dal Direttore di cantiere: da qui si prepara la comunicazione.</summary>
    Approvata = 1,
    /// <summary>Esito negativo: nessuna comunicazione.</summary>
    NonQualificato = 2,
}

/// <summary>
/// Stato delle procedure autorizzative collegate (es. autorizzazione al subappalto), gestito a parte: l'esito della
/// qualificazione interna non le sostituisce.
/// </summary>
public enum StatoAutorizzazione
{
    NonNecessaria = 0,
    DaRichiedere = 1,
    Richiesta = 2,
    Autorizzata = 3,
    Negata = 4,
}

/// <summary>
/// Qualificazione interna di un fornitore, subappaltatore o sub-affidatario per una commessa. In istruttoria si compila
/// l'elenco dei documenti verificati; l'approvazione del Direttore di cantiere fissa esito e data e non si modifica più.
/// </summary>
public sealed class QualificaFornitore : EntitaBase
{
    public Guid CommessaId { get; set; }
    public Guid FornitoreId { get; set; }

    /// <summary>Ragione sociale dall'anagrafica, aggiornata fino all'approvazione.</summary>
    public string Fornitore { get; set; } = "";

    public TipoOperatore Tipo { get; set; }

    /// <summary>Pacchetto di riferimento, facoltativo: ne propone l'oggetto delle prestazioni.</summary>
    public Guid? PacchettoId { get; set; }

    public string? OggettoPrestazioni { get; set; }

    /// <summary>Riferimento al fascicolo documentale (es. cartella o protocollo interno), facoltativo.</summary>
    public string? RiferimentoFascicolo { get; set; }

    public string? Prescrizioni { get; set; }
    public string? Note { get; set; }

    public StatoQualifica Stato { get; set; }

    /// <summary>Data di conclusione della qualificazione.</summary>
    public DateOnly? DataConclusione { get; set; }

    /// <summary>Direttore di cantiere che ha curato e approvato la qualificazione (dagli Attori della commessa o scritto a mano).</summary>
    public Guid? DirettoreCantiereId { get; set; }
    public string? DirettoreCantiere { get; set; }

    /// <summary>Utente che ha registrato l'approvazione in Corico, e quando.</summary>
    public string? ApprovataDa { get; set; }
    public DateTime? ApprovataIl { get; set; }

    public StatoAutorizzazione Autorizzazione { get; set; }
    public string? RiferimentoAutorizzazione { get; set; }
    public DateOnly? DataAutorizzazione { get; set; }

    public List<DocumentoQualifica> Documenti { get; } = [];
}

/// <summary>Documento verificato nella qualificazione (DURC, visura, POS…), con il file dell'archivio se caricato.</summary>
public sealed class DocumentoQualifica : EntitaBase
{
    public Guid QualificaId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string Descrizione { get; set; } = "";
    public bool Verificato { get; set; }
    public DateOnly? Scadenza { get; set; }
    public Guid? DocumentoId { get; set; }

    /// <summary>Documento riservato del fornitore: resta nel fascicolo e non si propone come allegato.</summary>
    public bool Riservato { get; set; }
}

public enum StatoComunicazione
{
    /// <summary>Generata all'approvazione o a mano: dati, destinatari e allegati modificabili.</summary>
    Bozza = 0,
    /// <summary>PDF definitivo generato, numerato e archiviato: il contenuto non cambia più.</summary>
    PdfDefinitivo = 1,
    ApprovataPerInvio = 2,
    /// <summary>Trasmissione registrata dall'operatore (data, modalità, destinatari effettivi, protocollo).</summary>
    Inviata = 3,
    /// <summary>Ricevuta di consegna o conferma acquisita.</summary>
    RicevutaAcquisita = 4,
    Annullata = 5,
}

public enum ModalitaInvio
{
    Pec = 0,
    Email = 1,
    Raccomandata = 2,
    ConsegnaAMano = 3,
    Altro = 4,
}

/// <summary>
/// Comunicazione formale dell'esito positivo della qualificazione al Direttore dei Lavori, al RUP e al CSE.
/// La generazione del PDF non vale come invio: la trasmissione si registra a parte.
/// </summary>
public sealed class ComunicazioneQualifica : EntitaBase
{
    public Guid CommessaId { get; set; }
    public Guid QualificaId { get; set; }

    /// <summary>Anno e progressivo assegnati al PDF definitivo (zero in bozza).</summary>
    public int Anno { get; set; }
    public int Progressivo { get; set; }
    public string Numero { get; set; } = "";

    public DateOnly DataEmissione { get; set; }
    public string Oggetto { get; set; } = "";

    /// <summary>Dati integrativi e note libere stampati nella lettera.</summary>
    public string? Note { get; set; }

    /// <summary>Limitazioni o prescrizioni, proposte dalla qualificazione e correggibili in bozza.</summary>
    public string? Prescrizioni { get; set; }

    public bool AllegaAttestato { get; set; } = true;
    public bool AllegaElencoDocumenti { get; set; } = true;

    public StatoComunicazione Stato { get; set; }

    /// <summary>PDF definitivo nell'archivio documenti (collegato alla commessa).</summary>
    public Guid? DocumentoId { get; set; }
    public DateTime? GenerataIl { get; set; }
    public string? GenerataDa { get; set; }

    public DateTime? ApprovataIl { get; set; }
    public string? ApprovataDa { get; set; }

    public DateOnly? InviataIl { get; set; }
    public ModalitaInvio? Modalita { get; set; }
    public string? DestinatariEffettivi { get; set; }
    public string? RiferimentoInvio { get; set; }
    public string? InvioRegistratoDa { get; set; }

    public DateOnly? RicevutaIl { get; set; }
    public string? RiferimentoRicevuta { get; set; }
    public Guid? RicevutaDocumentoId { get; set; }

    public DateOnly? AnnullataIl { get; set; }
    public string? MotivoAnnullamento { get; set; }

    public List<DestinatarioComunicazione> Destinatari { get; } = [];
    public List<AllegatoComunicazione> Allegati { get; } = [];

    public static string FormattaNumero(int anno, int progressivo) => $"CQ {anno}/{progressivo:000}";

    public string NumeroVisibile => Progressivo == 0 ? "Bozza" : Numero;
}

/// <summary>Destinatario per ruolo (DL, RUP, CSE), copiato dagli Attori della commessa e completabile a mano.</summary>
public sealed class DestinatarioComunicazione : EntitaBase
{
    public Guid ComunicazioneId { get; set; }
    public Guid CommessaId { get; set; }

    /// <summary>Ruolo come in <see cref="Corico.Core.Dominio.RuoloCommessa"/>.</summary>
    public RuoloCommessa Ruolo { get; set; }

    public Guid? AttoreId { get; set; }
    public bool Selezionato { get; set; } = true;
    public string? Titolo { get; set; }
    public string? Nominativo { get; set; }
    public string? Ente { get; set; }
    public string? Indirizzo { get; set; }
    public string? Pec { get; set; }
    public string? Email { get; set; }
}

/// <summary>Documento dell'archivio allegato alla comunicazione, scelto dall'operatore.</summary>
public sealed class AllegatoComunicazione : EntitaBase
{
    public Guid ComunicazioneId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public Guid DocumentoId { get; set; }
    public string Descrizione { get; set; } = "";
}
