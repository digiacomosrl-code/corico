using System.Globalization;
using System.Text;

namespace Corico.Core.Servizi;

/// <summary>
/// Ricerca per parole usata nei campi di scelta (clienti, fornitori): ogni parola scritta deve comparire
/// nel testo, in qualsiasi ordine, senza distinguere maiuscole e accenti.
/// </summary>
public static class RicercaTesto
{
    public static string[] Parole(string? ricerca) =>
        Normalizza(ricerca).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static bool Corrisponde(string? testo, string[] parole)
    {
        if (parole.Length == 0) return true;
        var t = Normalizza(testo);
        return parole.All(t.Contains);
    }

    /// <summary>Minuscolo, senza accenti e con la punteggiatura trasformata in spazi ("S.R.L." → "srl", "dell'Industria" → "dell industria").</summary>
    public static string Normalizza(string? testo)
    {
        if (string.IsNullOrEmpty(testo)) return "";
        var sb = new StringBuilder(testo.Length);
        foreach (var c in testo.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (c == '.') continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString();
    }
}
