using System.Globalization;
using System.Text;

namespace Corico.Modules.Documenti.Servizi;

/// <summary>
/// Duplicati simili (specifica Modulo 3, sez. 4): firma MinHash sulle sequenze di 5 parole del testo.
/// Due documenti con la stessa offerta rimandata o riscansionata hanno firme quasi uguali.
/// </summary>
public static class Somiglianza
{
    public const int Valori = 64;
    private const int ParolePerSequenza = 5;
    private const int MinimoParole = 40;

    /// <summary>Soglia oltre la quale due documenti sono segnalati come simili.</summary>
    public const double Soglia = 0.8;

    /// <summary>Firma in esadecimale (64 valori da 32 bit); null se il testo è troppo breve per un confronto affidabile.</summary>
    public static string? Firma(string testo)
    {
        var parole = Parole(testo);
        if (parole.Count < MinimoParole) return null;
        var minimi = Enumerable.Repeat(uint.MaxValue, Valori).ToArray();
        for (var i = 0; i + ParolePerSequenza <= parole.Count; i++)
        {
            var h = Fnv(string.Join(' ', parole.Skip(i).Take(ParolePerSequenza)));
            for (var k = 0; k < Valori; k++)
            {
                var v = Mescola(h, (uint)k);
                if (v < minimi[k]) minimi[k] = v;
            }
        }
        return string.Concat(minimi.Select(v => v.ToString("x8")));
    }

    /// <summary>Somiglianza stimata tra 0 e 1 (quota di valori uguali delle due firme).</summary>
    public static double Confronta(string firmaA, string firmaB)
    {
        if (firmaA.Length != firmaB.Length) return 0;
        var uguali = 0;
        for (var k = 0; k < firmaA.Length; k += 8)
            if (string.CompareOrdinal(firmaA, k, firmaB, k, 8) == 0) uguali++;
        return uguali / (double)(firmaA.Length / 8);
    }

    /// <summary>Parole in minuscolo senza accenti né punteggiatura, così a capo e impaginazione non contano.</summary>
    internal static List<string> Parole(string testo)
    {
        var senzaAccenti = new StringBuilder(testo.Length);
        foreach (var c in testo.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                senzaAccenti.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return senzaAccenti.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) { h ^= c; h *= 16777619u; }
        return h;
    }

    private static uint Mescola(uint h, uint seme)
    {
        var x = h ^ (seme * 0x9E3779B9u);
        x ^= x >> 16; x *= 0x85EBCA6Bu; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
        return x;
    }
}
