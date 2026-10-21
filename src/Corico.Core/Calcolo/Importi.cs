namespace Corico.Core.Calcolo;

/// <summary>
/// Regole numeriche comuni a tutta l'applicazione (sez. 16.4).
/// Tutti gli importi sono decimal, mai virgola mobile. I calcoli interni conservano
/// la precisione piena; l'arrotondamento a 2 decimali (metà per eccesso, come Excel)
/// si applica solo in visualizzazione e nei totali dichiarati.
/// </summary>
public static class Importi
{
    public const int DecimaliEuro = 2;

    /// <summary>Arrotondamento commerciale a 2 decimali (0,005 → 0,01), come la funzione ARROTONDA di Excel.</summary>
    public static decimal Arrotonda(decimal valore, int decimali = DecimaliEuro) =>
        Math.Round(valore, decimali, MidpointRounding.AwayFromZero);

    /// <summary>Rapporto percentuale; null ("n.d.") se il denominatore è zero o un valore manca.</summary>
    public static decimal? Percentuale(decimal? numeratore, decimal? denominatore)
    {
        if (numeratore is null || denominatore is null || denominatore == 0m) return null;
        return numeratore.Value / denominatore.Value * 100m;
    }

    /// <summary>Margine sul ricavo = (ricavo − costo) / ricavo × 100.</summary>
    public static decimal? MargineSulRicavo(decimal? ricavo, decimal? costo) =>
        ricavo is null || costo is null ? null : Percentuale(ricavo - costo, ricavo);

    /// <summary>Ricarico sul costo = (ricavo − costo) / costo × 100.</summary>
    public static decimal? RicaricoSulCosto(decimal? ricavo, decimal? costo) =>
        ricavo is null || costo is null ? null : Percentuale(ricavo - costo, costo);
}
