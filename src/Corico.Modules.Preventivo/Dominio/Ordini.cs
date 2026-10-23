using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

public enum StatoOrdine
{
    Emesso = 0,
    Annullato = 1,
    /// <summary>In preparazione: modificabile ed eliminabile, senza numero (Modulo 2, rilascio 2A).</summary>
    Bozza = 2,
}

/// <summary>
/// Ordine di acquisto al fornitore. Si prepara in bozza dal pacchetto o dalla Lista della Spesa (anche con righe di più
/// pacchetti dello stesso fornitore); all'emissione riceve il numero progressivo dell'anno (es. «ODA 2026/001») e diventa
/// immutabile: si può solo annullare, con un motivo, ed emetterne uno nuovo.
/// </summary>
public sealed class OrdineAcquisto : EntitaBase, IImmutabile
{
    public Guid CommessaId { get; set; }

    /// <summary>Pacchetto da cui è stato preparato l'ordine, se uno solo; null per gli ordini dalla Lista della Spesa su più pacchetti.</summary>
    public Guid? PacchettoId { get; set; }

    /// <summary>Offerta scelta del pacchetto al momento della preparazione, se presente.</summary>
    public Guid? OffertaId { get; set; }

    /// <summary>Anno e progressivo: zero finché l'ordine è in bozza (le bozze non consumano numeri).</summary>
    public int Anno { get; set; }
    public int Progressivo { get; set; }
    public string Numero { get; set; } = "";
    public DateOnly Data { get; set; }

    public Guid? FornitoreId { get; set; }
    public string Fornitore { get; set; } = "";

    /// <summary>Vero se il fornitore viene dall'offerta scelta e non si può cambiare nella bozza.</summary>
    public bool FornitoreDaOfferta { get; set; }

    /// <summary>Indirizzo e dati fiscali del fornitore al momento dell'emissione.</summary>
    public string? DatiFornitore { get; set; }

    public string Oggetto { get; set; } = "";
    public string? RiferimentoOfferta { get; set; }
    public string? Pagamento { get; set; }
    public string? Consegna { get; set; }
    public string? LuogoConsegna { get; set; }
    public string? Note { get; set; }
    public string? Condizioni { get; set; }

    public decimal Totale { get; set; }
    public StatoOrdine Stato { get; set; }
    public string? MotivoAnnullamento { get; set; }
    public DateOnly? AnnullatoIl { get; set; }

    /// <summary>Chi ha emesso l'ordine (l'autore della bozza è <see cref="EntitaBase.CreatoDa"/>).</summary>
    public string? EmessoDa { get; set; }

    /// <summary>Facoltativi dopo l'emissione: invio al fornitore e conferma d'ordine ricevuta.</summary>
    public DateOnly? InviatoIl { get; set; }
    public DateOnly? ConfermatoIl { get; set; }
    public Guid? ConfermaDocumentoId { get; set; }

    public List<RigaOrdine> Righe { get; } = [];

    public static string FormattaNumero(int anno, int progressivo) => $"ODA {anno}/{progressivo:000}";

    /// <summary>Numero da mostrare: «Bozza» finché non è emesso.</summary>
    public string NumeroVisibile => Stato == StatoOrdine.Bozza ? "Bozza" : Numero;
}

public sealed class RigaOrdine : EntitaBase, IImmutabile
{
    public Guid OrdineId { get; set; }
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string? Codice { get; set; }
    public string Descrizione { get; set; } = "";
    public string? Unita { get; set; }
    public decimal Quantita { get; set; }
    public decimal Prezzo { get; set; }
    public decimal Importo { get; set; }

    /// <summary>Codici WBS delle voci da cui proviene la riga (es. «2.1, 3.1.4»), copiati alla preparazione; restano com'erano all'emissione.</summary>
    public string? Wbs { get; set; }

    /// <summary>Categoria della risorsa nella Lista della Spesa; null per le righe libere (trasporto, imballo…).</summary>
    public CategoriaRisorsa? Categoria { get; set; }

    /// <summary>Prezzo della pianificazione al momento della preparazione: un prezzo diverso richiede una nota.</summary>
    public decimal? PrezzoPianificato { get; set; }

    /// <summary>Motivo di un prezzo diverso dal pianificato o di una quantità oltre il pianificato.</summary>
    public string? NotaScostamento { get; set; }

    /// <summary>Pacchetto a cui si imputa il costo di una riga libera (le righe dalla Lista della Spesa lo ricavano dalle origini).</summary>
    public Guid? PacchettoId { get; set; }

    public List<RigaOrdineOrigine> Origini { get; } = [];
}

/// <summary>
/// Ripartizione di una riga d'ordine sulle righe della Lista della Spesa da cui proviene (una per voce):
/// la stessa risorsa di più voci si ordina in un'unica riga, ma Corico conserva la quantità per voce.
/// </summary>
public sealed class RigaOrdineOrigine : EntitaBase, IImmutabile
{
    public Guid RigaOrdineId { get; set; }
    public Guid OrdineId { get; set; }
    public Guid CommessaId { get; set; }

    /// <summary>Chiave stabile della riga della Lista della Spesa (pacchetto, voce, categoria e risorsa).</summary>
    public string Chiave { get; set; } = "";

    public Guid PacchettoId { get; set; }
    public Guid? VoceComputoId { get; set; }
    public decimal Quantita { get; set; }
}
