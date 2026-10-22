using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

public enum StatoOrdine
{
    Emesso = 0,
    Annullato = 1,
}

/// <summary>
/// Ordine di acquisto al fornitore, emesso da un pacchetto con l'offerta scelta. Numero progressivo per anno
/// (es. «ODA 2026/001»). Dopo l'emissione è immutabile: si può solo annullare, con un motivo, ed emetterne uno nuovo.
/// </summary>
public sealed class OrdineAcquisto : EntitaBase, IImmutabile
{
    public Guid CommessaId { get; set; }
    public Guid PacchettoId { get; set; }
    public Guid OffertaId { get; set; }

    public int Anno { get; set; }
    public int Progressivo { get; set; }
    public string Numero { get; set; } = "";
    public DateOnly Data { get; set; }

    public Guid? FornitoreId { get; set; }
    public string Fornitore { get; set; } = "";

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

    public List<RigaOrdine> Righe { get; } = [];

    public static string FormattaNumero(int anno, int progressivo) => $"ODA {anno}/{progressivo:000}";
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
}
