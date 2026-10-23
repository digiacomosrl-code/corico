namespace Corico.Core.Dominio;

public enum EsitoRichiestaAi
{
    Riuscita = 0,
    Errore = 1,
    Rifiutata = 2,
}

/// <summary>
/// Registro delle richieste inviate al servizio AI esterno (Modulo 3, §3 e §9): chi, quando, per quale funzione e commessa,
/// cosa è stato inviato e quanto è costato. Ogni invio resta registrato, anche se non riuscito.
/// </summary>
public sealed class RichiestaAiRegistrata
{
    public Guid Id { get; set; }
    public DateTime DataOra { get; set; }
    public string Utente { get; set; } = "";
    public string Funzione { get; set; } = "";
    public Guid? CommessaId { get; set; }
    public string Modello { get; set; } = "";

    /// <summary>Testo inviato, identico a quello mostrato nell'anteprima.</summary>
    public string Contenuto { get; set; } = "";

    public int CaratteriInviati { get; set; }
    public int TokenIngresso { get; set; }
    public int TokenUscita { get; set; }

    /// <summary>Costo in euro calcolato dai token effettivi.</summary>
    public decimal Costo { get; set; }

    public EsitoRichiestaAi Esito { get; set; }
    public string? Messaggio { get; set; }
    public int DurataSecondi { get; set; }
}
