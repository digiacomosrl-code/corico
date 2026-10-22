namespace Corico.Modules.Preventivo.Importazione;

/// <summary>
/// Normalizzazione delle unità di misura scritte in modi diversi (sez. 12.1).
/// Il valore originale viene sempre conservato accanto a quello normalizzato.
/// </summary>
public static class UnitaMisura
{
    private static readonly Dictionary<string, string> Equivalenze = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cad"] = "cad", ["cad."] = "cad", ["cadauno"] = "cad", ["cd"] = "cad", ["n"] = "cad", ["n."] = "cad",
        ["nr"] = "cad", ["nr."] = "cad", ["num"] = "cad", ["pz"] = "cad",
        ["m2"] = "mq", ["mq"] = "mq", ["m²"] = "mq", ["mq."] = "mq",
        ["m3"] = "mc", ["mc"] = "mc", ["m³"] = "mc", ["mc."] = "mc",
        ["m"] = "m", ["ml"] = "m", ["m."] = "m", ["mt"] = "m",
        ["h"] = "h", ["ora"] = "h", ["ore"] = "h",
        ["kg"] = "kg", ["kg."] = "kg",
        ["a corpo"] = "a corpo", ["ac"] = "a corpo", ["a.c."] = "a corpo", ["corpo"] = "a corpo",
    };

    public static string? Normalizza(string? originale)
    {
        if (string.IsNullOrWhiteSpace(originale)) return null;
        var pulita = string.Join(' ', originale.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Equivalenze.TryGetValue(pulita, out var norm) ? norm : pulita;
    }
}
