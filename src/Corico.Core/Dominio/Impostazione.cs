namespace Corico.Core.Dominio;

/// <summary>Impostazione applicativa salvata nel database (chiave/valore).</summary>
public sealed class Impostazione
{
    public string Chiave { get; set; } = "";
    public string? Valore { get; set; }
    public DateTime ModificatoIl { get; set; }
}
