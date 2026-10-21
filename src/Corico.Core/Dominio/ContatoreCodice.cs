namespace Corico.Core.Dominio;

/// <summary>
/// Ultimo progressivo assegnato per anno. Non viene mai decrementato,
/// così i codici delle commesse non sono mai riutilizzati.
/// </summary>
public sealed class ContatoreCodice
{
    public int Anno { get; set; }
    public int Ultimo { get; set; }
}
