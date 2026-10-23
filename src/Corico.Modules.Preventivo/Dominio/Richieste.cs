using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

/// <summary>Stato della risposta di un fornitore a una richiesta d'offerta.</summary>
public enum StatoRisposta
{
    DaInviare = 0,
    Inviata = 1,
    Ricevuta = 2,
    Declinata = 3,
}

/// <summary>
/// Richiesta d'offerta (Modulo 2, rilascio 2B): voci o righe della Lista della Spesa da quotare, senza prezzi dell'impresa
/// né prezzi di contratto, indirizzata a uno o più fornitori. Numero progressivo dell'anno su tutte le commesse (es. «RdO 2026/001»).
/// </summary>
public sealed class RichiestaOfferta : EntitaBase, ICancellazioneRiservata
{
    public Guid CommessaId { get; set; }

    /// <summary>Pacchetto da cui è stata preparata, se uno solo.</summary>
    public Guid? PacchettoId { get; set; }

    public int Anno { get; set; }
    public int Progressivo { get; set; }
    public string Numero { get; set; } = "";
    public DateOnly Data { get; set; }

    public string Oggetto { get; set; } = "";

    /// <summary>Data entro cui si chiede la risposta.</summary>
    public DateOnly? Scadenza { get; set; }

    public string? LuogoLavori { get; set; }
    public string? Note { get; set; }

    public List<RigaRichiesta> Righe { get; } = [];
    public List<DestinatarioRichiesta> Destinatari { get; } = [];
}

/// <summary>Voce richiesta: descrizione estesa, unità e quantità. Nessun prezzo.</summary>
public sealed class RigaRichiesta : EntitaBase
{
    public Guid RichiestaId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }

    /// <summary>Pacchetto e voce del computo di provenienza (la voce solo se la riga viene da una sola voce).</summary>
    public Guid? PacchettoId { get; set; }
    public Guid? VoceComputoId { get; set; }

    /// <summary>Chiavi delle righe della Lista della Spesa di provenienza, separate da «;».</summary>
    public string? Chiavi { get; set; }

    public string? Codice { get; set; }
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }
    public decimal Quantita { get; set; }
}

/// <summary>Fornitore a cui è indirizzata la richiesta, con lo stato della risposta.</summary>
public sealed class DestinatarioRichiesta : EntitaBase
{
    public Guid RichiestaId { get; set; }
    public Guid CommessaId { get; set; }
    public Guid? FornitoreId { get; set; }
    public string Fornitore { get; set; } = "";
    public StatoRisposta Stato { get; set; }
    public DateOnly? InviataIl { get; set; }
    public DateOnly? RispostaIl { get; set; }

    /// <summary>Offerta registrata alla risposta.</summary>
    public Guid? OffertaId { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// Importo stimato di un'esclusione o di un onere a carico dell'impresa indicato in un'offerta: entra solo nel confronto.
/// Scelta l'offerta, si può trasformare in un costo aggiuntivo del pacchetto (voce a corpo dell'offerta).
/// </summary>
public sealed class StimaEsclusione : EntitaBase
{
    public Guid OffertaId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string Descrizione { get; set; } = "";
    public decimal Importo { get; set; }

    /// <summary>Voce a corpo creata trasformando la stima in costo del pacchetto.</summary>
    public Guid? CostoACorpoId { get; set; }
}
