namespace Corico.Core.Dominio;

/// <summary>
/// Riga del registro delle modifiche (audit), sez. 4.1.
/// </summary>
public sealed class VoceStorico
{
    public long Id { get; set; }
    public DateTime DataOra { get; set; }
    public string Utente { get; set; } = "";

    /// <summary>Inserimento, Modifica, Eliminazione o un'operazione applicativa (es. CambioStato).</summary>
    public string Operazione { get; set; } = "";

    public string Entita { get; set; } = "";
    public Guid? EntitaId { get; set; }

    /// <summary>Commessa a cui si riferisce la modifica, per lo storico di commessa.</summary>
    public Guid? CommessaId { get; set; }

    public string? ValoriPrecedenti { get; set; }
    public string? ValoriSuccessivi { get; set; }
    public string? Nota { get; set; }
}
