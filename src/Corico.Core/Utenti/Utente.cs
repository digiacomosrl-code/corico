using Corico.Core.Dominio;

namespace Corico.Core.Utenti;

/// <summary>Profili di accesso (versione 1.0 multiutente).</summary>
public enum RuoloUtente
{
    /// <summary>Tutto, compresi utenti, backup, ripristino e aggiornamenti.</summary>
    Amministratore = 0,
    /// <summary>Consulta e modifica i dati delle commesse.</summary>
    Operatore = 1,
    /// <summary>Sola consultazione: nessuna modifica.</summary>
    Consultazione = 2,
}

/// <summary>Utente di Corico. La password è conservata solo come impronta PBKDF2 con sale casuale.</summary>
public sealed class Utente : EntitaBase
{
    public string NomeUtente { get; set; } = "";
    public string NomeCompleto { get; set; } = "";
    public string? Email { get; set; }
    public string HashPassword { get; set; } = "";
    public RuoloUtente Ruolo { get; set; }
    public bool Attivo { get; set; } = true;

    /// <summary>Vero dopo la creazione o la reimpostazione da parte dell'amministratore: al primo accesso si sceglie una nuova password.</summary>
    public bool CambioPasswordObbligatorio { get; set; }

    public DateTime? UltimoAccesso { get; set; }

    public IdentitaUtente Identita() => new(Id, NomeUtente, NomeCompleto, Ruolo, Attivo, CambioPasswordObbligatorio);
}
