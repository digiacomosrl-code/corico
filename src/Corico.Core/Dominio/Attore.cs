namespace Corico.Core.Dominio;

/// <summary>
/// Persona che ricopre una figura nelle commesse (RUP, direttore dei lavori, CSE, direttore di cantiere, capocantiere):
/// archivio unico, riusato da tutte le commesse e dalle comunicazioni (es. lettere via PEC).
/// </summary>
public sealed class Attore : EntitaBase, ICancellazioneRiservata
{
    public string Nominativo { get; set; } = "";

    /// <summary>Titolo da anteporre al nome nelle comunicazioni (es. «Ing.», «Arch.», «Geom.»).</summary>
    public string? Titolo { get; set; }

    /// <summary>Ente o società di appartenenza (es. «Comune di Caltagirone», «Studio Tecnico Rossi»).</summary>
    public string? Ente { get; set; }

    public string? Indirizzo { get; set; }
    public string? Cap { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? Email { get; set; }
    public string? Pec { get; set; }
    public string? Telefono { get; set; }
    public string? Note { get; set; }
    public bool Archiviato { get; set; }
}

/// <summary>Figure di commessa. I valori sono salvati nel database: non cambiarli, aggiungerne di nuovi in coda.</summary>
public enum RuoloCommessa
{
    Rup = 1,
    DirettoreLavori = 2,
    Cse = 3,
    DirettoreCantiere = 4,
    Capocantiere = 5,
}

public static class RuoliCommessa
{
    public static IReadOnlyList<RuoloCommessa> Tutti { get; } = Enum.GetValues<RuoloCommessa>();

    public static string Testo(RuoloCommessa r) => r switch
    {
        RuoloCommessa.Rup => "Responsabile unico del procedimento",
        RuoloCommessa.DirettoreLavori => "Direttore dei lavori",
        RuoloCommessa.Cse => "Coordinatore per la sicurezza in esecuzione",
        RuoloCommessa.DirettoreCantiere => "Direttore di cantiere",
        RuoloCommessa.Capocantiere => "Capocantiere",
        _ => r.ToString(),
    };

    public static string Sigla(RuoloCommessa r) => r switch
    {
        RuoloCommessa.Rup => "RUP",
        RuoloCommessa.DirettoreLavori => "DL",
        RuoloCommessa.Cse => "CSE",
        RuoloCommessa.DirettoreCantiere => "Dir. cantiere",
        RuoloCommessa.Capocantiere => "Capocantiere",
        _ => r.ToString(),
    };
}

/// <summary>
/// Un attore in una figura della commessa per un periodo. Più soggetti possono ricoprire la stessa figura;
/// un cambio in corso d'opera chiude l'incarico (data «Al») e ne apre uno nuovo, così resta lo storico.
/// </summary>
public sealed class IncaricoCommessa : EntitaBase
{
    public Guid CommessaId { get; set; }
    public Guid AttoreId { get; set; }
    public Attore? Attore { get; set; }
    public RuoloCommessa Ruolo { get; set; }

    /// <summary>Inizio dell'incarico (es. data della nomina); vuoto se non noto.</summary>
    public DateOnly? Dal { get; set; }

    /// <summary>Fine dell'incarico; vuoto finché è in carica.</summary>
    public DateOnly? Al { get; set; }

    /// <summary>Riferimenti della nomina o motivo della sostituzione.</summary>
    public string? Note { get; set; }
}
