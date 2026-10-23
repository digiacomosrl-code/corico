namespace Corico.Core.Dominio;

/// <summary>
/// Fornitore, subappaltatore o noleggiatore. Anagrafica comune a tutti i moduli
/// (preventivo, preventivi fornitori, consuntivo).
/// </summary>
public sealed class Fornitore : EntitaBase, ICancellazioneRiservata
{
    public string Denominazione { get; set; } = "";
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? Indirizzo { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }

    /// <summary>Lavorazioni o forniture trattate (es. "Infissi, Ponteggi"), per la ricerca.</summary>
    public string? Categorie { get; set; }

    public string? Note { get; set; }
    public bool Archiviato { get; set; }
}
