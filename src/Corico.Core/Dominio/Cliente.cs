namespace Corico.Core.Dominio;

public sealed class Cliente : EntitaBase, ICancellazioneRiservata
{
    public string Denominazione { get; set; } = "";
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? Indirizzo { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? Note { get; set; }
    public bool Archiviato { get; set; }
}
