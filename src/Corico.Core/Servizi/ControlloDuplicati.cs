using System.Text;
using System.Text.RegularExpressions;

namespace Corico.Core.Servizi;

public sealed record PossibileDuplicato(Guid Id, string Denominazione, string Motivo);

/// <summary>
/// Segnalazione di possibili duplicati nelle anagrafiche (clienti, fornitori): stessa partita IVA,
/// stesso codice fiscale o denominazione simile. La segnalazione non blocca il salvataggio.
/// </summary>
public static partial class ControlloDuplicati
{
    public static IReadOnlyList<PossibileDuplicato> Cerca(string? denominazione, string? partitaIva, string? codiceFiscale,
        IEnumerable<(Guid Id, string Denominazione, string? PartitaIva, string? CodiceFiscale)> esistenti)
    {
        var piva = NormalizzaCodice(partitaIva);
        var cf = NormalizzaCodice(codiceFiscale);
        var nome = NormalizzaNome(denominazione);
        var risultato = new List<PossibileDuplicato>();

        foreach (var e in esistenti)
        {
            if (piva is not null && NormalizzaCodice(e.PartitaIva) == piva)
                risultato.Add(new(e.Id, e.Denominazione, "Stessa partita IVA"));
            else if (cf is not null && NormalizzaCodice(e.CodiceFiscale) == cf)
                risultato.Add(new(e.Id, e.Denominazione, "Stesso codice fiscale"));
            else if (nome.Length >= 3 && NomiSimili(nome, NormalizzaNome(e.Denominazione)))
                risultato.Add(new(e.Id, e.Denominazione, "Denominazione simile"));
        }
        return risultato;
    }

    public static string? NormalizzaCodice(string? codice)
    {
        if (string.IsNullOrWhiteSpace(codice)) return null;
        var s = new string(codice.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (s.StartsWith("IT") && s.Length == 13) s = s[2..];
        return s.Length == 0 ? null : s;
    }

    public static string NormalizzaNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "";
        var s = nome.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        s = new string(s.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());
        s = FormeGiuridiche().Replace(" " + s + " ", " ");
        return SpaziMultipli().Replace(s, " ").Trim();
    }

    public static bool NomiSimili(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b) || b.Contains(a));

    [GeneratedRegex(@"\s(srl|srls|spa|sas|snc|sapa|scarl|scrl|soc|coop|societa|cooperativa|ditta|impresa)(?=\s)")]
    private static partial Regex FormeGiuridiche();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}
