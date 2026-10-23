using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

/// <summary>
/// Elemento della WBS della commessa (struttura delle lavorazioni, es. «2 Opere edili», «2.3 Pavimenti»): la gerarchia
/// è data dai punti del codice. È distinta dalle categorie del computo, dal codice del pacchetto e dai codici risorsa;
/// serve a leggere ricavi, costi e ordini per lavorazione e, nel consuntivo, a imputare fatture e ore.
/// Non è un prezzo contrattuale: resta modificabile anche dopo l'approvazione del budget.
/// </summary>
public sealed class ElementoWbs : EntitaBase
{
    public Guid CommessaId { get; set; }
    public string Codice { get; set; } = "";
    public string Descrizione { get; set; } = "";
}

/// <summary>Regole sui codici WBS: gerarchia per punti e ordinamento naturale (2.10 dopo 2.9).</summary>
public static class CodiciWbs
{
    public const int LunghezzaMassima = 50;

    /// <summary>Codice ripulito: spazi tolti, separatori uniformati al punto, nessun punto iniziale o finale.</summary>
    public static string? Normalizza(string? codice)
    {
        if (string.IsNullOrWhiteSpace(codice)) return null;
        var c = string.Concat(codice.Trim().Where(ch => !char.IsWhiteSpace(ch))).Replace(',', '.').Trim('.');
        while (c.Contains("..")) c = c.Replace("..", ".");
        return c.Length == 0 ? null : c;
    }

    /// <summary>Livello nella gerarchia: «2» = 1, «2.3» = 2, «6.2.1» = 3.</summary>
    public static int Livello(string codice) => codice.Count(c => c == '.') + 1;

    /// <summary>Codice del livello superiore («6.2.1» → «6.2»), null per il primo livello.</summary>
    public static string? Padre(string codice)
    {
        var i = codice.LastIndexOf('.');
        return i <= 0 ? null : codice[..i];
    }

    /// <summary>Vero se <paramref name="codice"/> è <paramref name="filtro"/> o un suo sottolivello («2.3.1» rientra in «2» e in «2.3»).</summary>
    public static bool Rientra(string? codice, string filtro) =>
        codice is not null && (string.Equals(codice, filtro, StringComparison.OrdinalIgnoreCase)
                               || codice.StartsWith(filtro + ".", StringComparison.OrdinalIgnoreCase));

    /// <summary>Confronto naturale per parti numeriche: 2 &lt; 2.1 &lt; 2.9 &lt; 2.10 &lt; 3.</summary>
    public static readonly IComparer<string?> Ordine = Comparer<string?>.Create(Confronta);

    private static int Confronta(string? a, string? b)
    {
        if (a is null || b is null) return a is null ? (b is null ? 0 : 1) : -1;
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var c = int.TryParse(pa[i], out var na) && int.TryParse(pb[i], out var nb)
                ? na.CompareTo(nb)
                : string.Compare(pa[i], pb[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }
}
