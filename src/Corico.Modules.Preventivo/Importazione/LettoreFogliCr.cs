using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Importazione;

// Lettura dei file Excel aziendali di pianificazione (fogli «CR» e tabellone «HOME»), sez. 14.1 e decisione 5.

public enum TipoRigaCr { Voce, CostoACorpo, Nota }

public sealed record RigaCr(int Riga, TipoRigaCr Tipo, string? Articolo, string? Descrizione, string? Unita,
    decimal Quantita, decimal Prezzo, decimal Fornitura, decimal FornituraEPosa, decimal ManodoperaENoli)
{
    public decimal CostoUnitario => Fornitura + FornituraEPosa + ManodoperaENoli;
    public decimal Costo => Quantita * CostoUnitario;
}

public sealed record FoglioCr(
    string Nome, string? StatoTesto, decimal? Ribasso, string? Descrizione, string? Fornitore,
    IReadOnlyList<RigaCr> Righe, decimal? RicavoNettoFoglio, decimal? CostoFoglio, decimal? IntegrazioneCm)
{
    public bool Vuoto => Righe.All(r => r.Tipo == TipoRigaCr.Nota) && (CostoFoglio ?? 0) == 0 && (RicavoNettoFoglio ?? 0) == 0;

    /// <summary>Stato del costo secondo la decisione 4: «Stato finale» e «Consuntivo al SAL» diventano Definitivo.</summary>
    public StatoCosto Stato => LettoreFogliCr.Stato(StatoTesto);

    public TipoOfferta TipoOfferta
    {
        get
        {
            var voci = Righe.Where(r => r.Tipo != TipoRigaCr.Nota).ToList();
            if (voci.Any(r => r.FornituraEPosa != 0)) return TipoOfferta.FornituraEPosa;
            if (voci.Any(r => r.Fornitura != 0)) return TipoOfferta.SolaFornitura;
            if (voci.Any(r => r.ManodoperaENoli != 0)) return TipoOfferta.SolaPosa;
            return TipoOfferta.StimaInterna;
        }
    }
}

public sealed record RigaHome(int Riga, string? Gruppo, string Attivita, string? Oda, string? Fornitore,
    string? FoglioCollegato, decimal? Ricavo, decimal? Costo, string? Note, string? Firma, DateOnly? DataNota);

public sealed class PianificazioneLetta
{
    public string? CodiceCommessa { get; init; }
    public string? NomeCommessa { get; init; }
    public List<FoglioCr> Fogli { get; } = [];
    public List<RigaHome> Home { get; } = [];
    public List<string> FogliIgnorati { get; } = [];
}

public static partial class LettoreFogliCr
{
    public static PianificazioneLetta Leggi(Stream xlsx)
    {
        var fogli = LettoreXlsx.Leggi(xlsx);
        var home = fogli.FirstOrDefault(f => f.Nome.Trim().Equals("HOME", StringComparison.OrdinalIgnoreCase));
        var letta = new PianificazioneLetta
        {
            CodiceCommessa = home is null ? null : TestoDopo(home, 2, "Codici di Commessa", 1),
            NomeCommessa = home is null ? null : TestoDopo(home, 2, "Codici di Commessa", 2),
        };

        foreach (var f in fogli.Where(f => f != home))
        {
            var cr = LeggiFoglio(f);
            if (cr is null || cr.Vuoto) letta.FogliIgnorati.Add(f.Nome);
            else letta.Fogli.Add(cr);
        }
        if (home is not null) LeggiHome(home, letta);
        if (letta.Fogli.Count == 0)
            throw new FormatException("Nel file non sono stati trovati fogli di pianificazione con la struttura dei fogli «CR».");
        return letta;
    }

    private static FoglioCr? LeggiFoglio(FoglioXlsx f)
    {
        var intestazione = TrovaIntestazione(f);
        if (intestazione is null) return null;
        var (rigaInt, col) = intestazione.Value;

        // Riga 2: stato, «Ribasso» con il valore, descrizione del pacchetto e fornitore.
        string? stato = null, descrizione = null, fornitore = null;
        decimal? ribasso = null;
        var colRibasso = Enumerable.Range(1, f.UltimaColonna).FirstOrDefault(c => Uguale(f.Testo(2, c), "Ribasso"));
        if (colRibasso > 0)
        {
            stato = Enumerable.Range(1, colRibasso - 1).Select(c => f.Cella(2, c)).LastOrDefault(c => c?.Valore is string)?.Testo;
            var c = colRibasso + 1;
            for (; c <= f.UltimaColonna && ribasso is null; c++) ribasso = f.Numero(2, c);
            var testi = Enumerable.Range(c, Math.Max(0, f.UltimaColonna - c + 1)).Select(x => f.Cella(2, x))
                .Where(x => x?.Valore is string).Select(x => x!.Testo).Where(t => t is not null).ToList();
            descrizione = testi.ElementAtOrDefault(0);
            fornitore = Segnaposto(testi.ElementAtOrDefault(1));
        }

        var righe = new List<RigaCr>();
        for (var r = rigaInt + 1; r <= f.UltimaRiga; r++)
        {
            var desc = Pulisci(f.Testo(r, col["Descrizione"]));
            if (desc is not null && desc.Equals("TOTALE", StringComparison.OrdinalIgnoreCase)) break;
            var art = Pulisci(f.Testo(r, col["Articolo"]));
            if (art is "0") art = null;
            if (desc is "0") desc = null;
            var unita = Pulisci(col.TryGetValue("U.M.", out var cu) ? f.Testo(r, cu) : null);
            var q = f.Numero(r, col["Q Tot"]) ?? 0;
            var prezzo = f.Numero(r, col["Prezzo"]) ?? 0;
            var forn = Numero(f, r, col, "Forn.");
            var fp = Numero(f, r, col, "Fornitura e Posa");
            var mo = Numero(f, r, col, "M.O. e Noli");
            if (art is null && desc is null && prezzo == 0 && (forn + fp + mo == 0 || q == 0)) continue;

            var tipo = prezzo != 0 || art is not null ? TipoRigaCr.Voce
                : q != 0 && forn + fp + mo != 0 ? TipoRigaCr.CostoACorpo
                : TipoRigaCr.Nota;
            righe.Add(new RigaCr(r, tipo, art, desc, unita, q, prezzo, forn, fp, mo));
        }

        return new FoglioCr(f.Nome, stato, ribasso, descrizione, fornitore, righe,
            NumeroDopo(f, "Ricavi da progetto netto"), NumeroDopo(f, "Totale Costo"), NumeroDopo(f, "Integrazione C.M."));
    }

    private static (int Riga, Dictionary<string, int> Colonne)? TrovaIntestazione(FoglioXlsx f)
    {
        for (var r = 1; r <= Math.Min(15, f.UltimaRiga); r++)
        {
            var colonne = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= f.UltimaColonna; c++)
            {
                var t = f.Testo(r, c);
                if (t is not null && !colonne.ContainsKey(t)) colonne[t] = c;
            }
            if (colonne.ContainsKey("Descrizione") && colonne.ContainsKey("Prezzo") && colonne.ContainsKey("Q Tot") && colonne.ContainsKey("Articolo"))
                return (r, colonne);
        }
        return null;
    }

    private static void LeggiHome(FoglioXlsx f, PianificazioneLetta letta)
    {
        int rigaInt = 0;
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r <= Math.Min(30, f.UltimaRiga) && rigaInt == 0; r++)
        {
            for (var c = 1; c <= f.UltimaColonna; c++)
                if (f.Testo(r, c) is { } t && Normalizza(t).StartsWith("ATTIVITA", StringComparison.Ordinal)) rigaInt = r;
            if (rigaInt == 0) continue;
            for (var c = 1; c <= f.UltimaColonna; c++)
                if (f.Testo(r, c) is { } t) col[Normalizza(t)] = c;
        }
        if (rigaInt == 0) return;

        int? C(string nome) => col.Keys.FirstOrDefault(k => k.StartsWith(nome, StringComparison.Ordinal)) is { } k ? col[k] : null;
        var cGruppo = C("GRUPPO"); var cAtt = C("ATTIVITA")!.Value; var cOda = C("ODA"); var cForn = C("FORNITORE");
        var cRic = C("RICAVO"); var cCosto = C("COSTO"); var cNote = C("NOTE"); var cFirma = C("FIRMA"); var cData = C("DATA");

        for (var r = rigaInt + 1; r <= f.UltimaRiga; r++)
        {
            var att = Pulisci(f.Testo(r, cAtt));
            if (att is null) continue;
            string? foglio = null;
            foreach (var c in new[] { cRic, cCosto, cForn })
            {
                if (c is null) continue;
                if (LettoreXlsx.RiferimentoEsterno(f.Cella(r, c.Value)?.Formula) is { } rif) { foglio = rif.Foglio; break; }
            }
            letta.Home.Add(new RigaHome(r,
                cGruppo is null ? null : Pulisci(f.Testo(r, cGruppo.Value)), att,
                cOda is null ? null : Pulisci(f.Testo(r, cOda.Value)),
                cForn is null ? null : Segnaposto(Pulisci(f.Testo(r, cForn.Value))), foglio,
                cRic is null ? null : f.Numero(r, cRic.Value), cCosto is null ? null : f.Numero(r, cCosto.Value),
                cNote is null ? null : Pulisci(f.Testo(r, cNote.Value)), cFirma is null ? null : Pulisci(f.Testo(r, cFirma.Value)),
                cData is null ? null : LettoreXlsx.Data(f.Cella(r, cData.Value))));
        }
    }

    public static StatoCosto Stato(string? testo)
    {
        var t = Normalizza(testo ?? "");
        if (t.Contains("FINALE") || t.Contains("CONSUNTIV") || t.Contains("DEFINITIV")) return StatoCosto.Definitivo;
        if (t.Contains("PROVV")) return StatoCosto.Provvisorio;
        return StatoCosto.Previsione;
    }

    /// <summary>Chiave di confronto per i codici tariffa: maiuscole, senza spazi, zeri iniziali dei segmenti numerici tolti (24.03.03.001 = 24.3.3.1).</summary>
    public static string? ChiaveTariffa(string? tariffa)
    {
        if (string.IsNullOrWhiteSpace(tariffa)) return null;
        var t = tariffa.Trim().TrimEnd('-', ' ', '?').ToUpperInvariant().Replace(" ", "");
        return string.Join('.', t.Split('.').Select(s => s.All(char.IsDigit) && s.Length > 0 ? s.TrimStart('0').PadLeft(1, '0') : s));
    }

    /// <summary>Testo in maiuscolo senza accenti, apostrofi e spazi multipli, per confrontare nomi di pacchetti e fogli.</summary>
    public static string Normalizza(string testo)
    {
        var d = testo.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Spazi().Replace(sb.ToString().ToUpperInvariant().Replace("'", "").Replace("_", " "), " ").Trim();
    }

    private static decimal Numero(FoglioXlsx f, int r, Dictionary<string, int> col, string nome) =>
        col.TryGetValue(nome, out var c) ? f.Numero(r, c) ?? 0 : 0;

    private static decimal? NumeroDopo(FoglioXlsx f, string etichetta)
    {
        // Il riepilogo è in fondo al foglio; il confronto è esatto perché l'intestazione usa «Totale costo».
        foreach (var ((r, c), cella) in f.Celle.OrderByDescending(x => x.Key.Riga))
        {
            if (cella.Testo != etichetta) continue;
            for (var k = c + 1; k <= Math.Min(f.UltimaColonna, c + 8); k++)
                if (f.Numero(r, k) is { } n) return n;
        }
        return null;
    }

    private static string? TestoDopo(FoglioXlsx f, int riga, string etichetta, int posizione)
    {
        var c0 = Enumerable.Range(1, f.UltimaColonna).FirstOrDefault(c => Uguale(f.Testo(riga, c), etichetta));
        if (c0 == 0) return null;
        return Enumerable.Range(c0 + 1, Math.Max(0, f.UltimaColonna - c0)).Select(c => f.Testo(riga, c)).Where(t => t is not null)
            .ElementAtOrDefault(posizione - 1);
    }

    private static bool Uguale(string? a, string b) => a is not null && a.Trim().Equals(b, StringComparison.OrdinalIgnoreCase);

    private static string? Pulisci(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return Spazi().Replace(s.Replace('\t', ' '), " ").Trim();
    }

    /// <summary>Nei fogli i fornitori non ancora definiti sono scritti come «xxx», «???», «:::».</summary>
    private static string? Segnaposto(string? s) =>
        s is null || s.All(ch => ch is 'x' or 'X' or '?' or ':' or '.' or ' ' or '-') ? null : s;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spazi();
}
