using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Corico.Modules.Preventivo.Importazione;

/// <summary>Cella letta da un file Excel: valore memorizzato dall'ultimo calcolo ed eventuale formula.</summary>
public sealed record CellaXlsx(object? Valore, string? Formula)
{
    public decimal? Numero => Valore switch
    {
        decimal d => d,
        string s when decimal.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public string? Testo => Valore switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        _ => Valore.ToString(),
    };
}

public sealed class FoglioXlsx(string nome)
{
    public string Nome { get; } = nome;
    public Dictionary<(int Riga, int Colonna), CellaXlsx> Celle { get; } = [];
    public int UltimaRiga { get; internal set; }
    public int UltimaColonna { get; internal set; }

    public CellaXlsx? Cella(int riga, int colonna) => Celle.GetValueOrDefault((riga, colonna));
    public CellaXlsx? Cella(string riferimento) { var (r, c) = LettoreXlsx.Coordinate(riferimento); return Cella(r, c); }
    public string? Testo(int riga, int colonna) => Cella(riga, colonna)?.Testo;
    public decimal? Numero(int riga, int colonna) => Cella(riga, colonna)?.Numero;
}

/// <summary>
/// Lettura essenziale dei file .xlsx (Office Open XML) in sola lettura dei valori e delle formule.
/// Non interpreta tabelle, stili o grafici: i file aziendali contengono definizioni di tabella non valide
/// che bloccano le librerie complete (sez. 12.2), mentre i valori restano sempre leggibili.
/// </summary>
public static partial class LettoreXlsx
{
    public static IReadOnlyList<FoglioXlsx> Leggi(Stream contenuto)
    {
        using var zip = new ZipArchive(contenuto, ZipArchiveMode.Read, leaveOpen: true);
        var condivise = LeggiStringheCondivise(zip);
        var fogli = new List<FoglioXlsx>();

        var relazioni = new Dictionary<string, string>();
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (rels is not null)
        {
            using var r = XmlReader.Create(rels.Open());
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                    relazioni[r.GetAttribute("Id")!] = r.GetAttribute("Target")!;
        }

        var workbook = zip.GetEntry("xl/workbook.xml") ?? throw new FormatException("Il file non è una cartella di lavoro Excel (.xlsx).");
        var elenco = new List<(string Nome, string Percorso)>();
        using (var r = XmlReader.Create(workbook.Open()))
        {
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.LocalName != "sheet") continue;
                var nome = r.GetAttribute("name")!;
                var id = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                if (id is null || !relazioni.TryGetValue(id, out var target)) continue;
                var percorso = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                elenco.Add((nome, percorso));
            }
        }

        foreach (var (nome, percorso) in elenco)
        {
            var voce = zip.GetEntry(percorso);
            if (voce is null) continue;
            var foglio = new FoglioXlsx(nome);
            LeggiFoglio(voce, condivise, foglio);
            fogli.Add(foglio);
        }
        return fogli;
    }

    private static List<string> LeggiStringheCondivise(ZipArchive zip)
    {
        var elenco = new List<string>();
        var voce = zip.GetEntry("xl/sharedStrings.xml");
        if (voce is null) return elenco;
        foreach (var si in Elementi(voce, "si"))
            elenco.Add(TestoRicco(si));
        return elenco;
    }

    /// <summary>Concatena i nodi &lt;t&gt; (testo semplice o formattato), escludendo la pronuncia fonetica &lt;rPh&gt;.</summary>
    private static string TestoRicco(XElement e) =>
        string.Concat(e.Descendants().Where(d => d.Name.LocalName == "t" && d.Parent?.Name.LocalName != "rPh").Select(d => d.Value));

    /// <summary>Scorre gli elementi con il nome indicato senza caricare l'intero XML in memoria.</summary>
    private static IEnumerable<XElement> Elementi(ZipArchiveEntry voce, string nome)
    {
        using var r = XmlReader.Create(voce.Open());
        r.MoveToContent();
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == nome)
                yield return (XElement)XNode.ReadFrom(r);
            else
                r.Read();
        }
    }

    private static void LeggiFoglio(ZipArchiveEntry voce, List<string> condivise, FoglioXlsx foglio)
    {
        foreach (var c in Elementi(voce, "c"))
        {
            var rif = (string?)c.Attribute("r");
            if (rif is null) continue;
            var tipo = (string?)c.Attribute("t");
            string? valore = null, formula = null, inline = null;
            foreach (var figlio in c.Elements())
            {
                switch (figlio.Name.LocalName)
                {
                    case "v": valore = figlio.Value; break;
                    case "f": formula = string.IsNullOrEmpty(figlio.Value) ? null : figlio.Value; break;
                    case "is": inline = TestoRicco(figlio); break;
                }
            }

            object? v = tipo switch
            {
                "s" when int.TryParse(valore, out var i) && i < condivise.Count => condivise[i],
                "inlineStr" => inline,
                "str" => valore,
                "b" => valore == "1",
                "e" => null,
                _ when valore is not null && decimal.TryParse(valore, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                _ => valore,
            };
            if (v is null && formula is null) continue;
            var (riga, colonna) = Coordinate(rif);
            foglio.Celle[(riga, colonna)] = new CellaXlsx(v, formula is null ? null : "=" + formula);
            foglio.UltimaRiga = Math.Max(foglio.UltimaRiga, riga);
            foglio.UltimaColonna = Math.Max(foglio.UltimaColonna, colonna);
        }
    }

    public static (int Riga, int Colonna) Coordinate(string riferimento)
    {
        var colonna = 0;
        var i = 0;
        var rif = riferimento.Replace("$", "");
        for (; i < rif.Length && char.IsLetter(rif[i]); i++)
            colonna = colonna * 26 + (char.ToUpperInvariant(rif[i]) - 'A' + 1);
        return (int.Parse(rif[i..], CultureInfo.InvariantCulture), colonna);
    }

    /// <summary>Riferimento a un altro foglio in una formula semplice, es. <c>='CR VetrateInterne'!$O$39</c> o <c>=CR_Piattaforma_elev!$O$49</c>.</summary>
    public static (string Foglio, string Cella)? RiferimentoEsterno(string? formula)
    {
        if (formula is null) return null;
        var m = RiferimentoFoglio().Match(formula);
        if (!m.Success) return null;
        var foglio = m.Groups["q"].Success ? m.Groups["q"].Value.Replace("''", "'") : m.Groups["n"].Value;
        return (foglio, m.Groups["c"].Value.Replace("$", ""));
    }

    [GeneratedRegex(@"^=\+?(?:'(?<q>(?:[^']|'')+)'|(?<n>[^'!=+\s]+))!(?<c>\$?[A-Z]+\$?\d+)$")]
    private static partial Regex RiferimentoFoglio();

    public static DateOnly? Data(CellaXlsx? cella) =>
        cella?.Numero is { } n && n is > 0 and < 2958466 ? DateOnly.FromDateTime(DateTime.FromOADate((double)n)) : null;
}
