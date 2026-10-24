using System.Globalization;
using System.Text;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Importazione;

/// <summary>Analisi letta dal file, con il numero di riga della testata e gli errori che ne impediscono l'importazione.</summary>
public sealed record AnalisiLetta(string File, int Riga, AnalisiAziendale Analisi, List<string> Errori)
{
    public bool Valida => Errori.Count == 0;
}

public sealed record FileAnalisiLetto(IReadOnlyList<AnalisiLetta> Analisi, IReadOnlyList<string> Avvisi, int VociEscluse);

/// <summary>
/// Lettura delle analisi costo da file (tracciato «testata + righe», es. SIC24_Import_CORICO):
/// un .xlsx con i fogli Analisi e RigheAnalisi, oppure i due CSV corrispondenti.
/// Le tabelle si riconoscono dalle intestazioni; le colonne di controllo (costo unitario, prezzo del prezzario…) si ignorano.
/// I fogli Risorse ed Escluse, se presenti, non si importano.
/// </summary>
public static class LettoreAnalisi
{
    public const int LunghezzaDescrizione = 500;
    public const int LunghezzaUnita = 30;

    private sealed record Tabella(string File, IReadOnlyList<string> Intestazioni, IReadOnlyList<(int Riga, IReadOnlyList<string> Valori)> Righe)
    {
        private readonly Dictionary<string, int> _colonne = Intestazioni.Select((t, i) => (Chiave(t), i))
            .GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().i);

        public bool Ha(string colonna) => _colonne.ContainsKey(colonna);

        public string? Valore(IReadOnlyList<string> riga, params string[] colonne)
        {
            foreach (var c in colonne)
                if (_colonne.TryGetValue(c, out var i) && i < riga.Count && !string.IsNullOrWhiteSpace(riga[i]))
                    return riga[i].Trim();
            return null;
        }
    }

    public static FileAnalisiLetto Leggi(IEnumerable<(string Nome, Stream Contenuto)> file)
    {
        var tabelle = new List<Tabella>();
        foreach (var (nome, contenuto) in file)
            tabelle.AddRange(Tabelle(nome, contenuto));

        var testate = tabelle.Where(t => t.Ha("codice") && t.Ha("descrizione") && !t.Ha("codiceanalisi") && !t.Ha("motivo")).ToList();
        var righe = tabelle.Where(t => t.Ha("codiceanalisi")).ToList();
        var escluse = tabelle.Where(t => t.Ha("codice") && t.Ha("motivo")).Sum(t => t.Righe.Count);
        if (testate.Count == 0)
            throw new OperazioneNonValidaException(
                "Nel file non c'è la tabella delle analisi (colonne Codice, Descrizione, Unita, Stato…). Scegli il file .xlsx del tracciato oppure il CSV delle analisi insieme a quello delle righe.");
        if (righe.Count == 0)
            throw new OperazioneNonValidaException(
                "Nel file non c'è la tabella delle risorse (colonne CodiceAnalisi, Categoria, Descrizione, Fabbisogno, Prezzo…). Se usi i CSV, scegli insieme i due file Analisi e RigheAnalisi.");

        var avvisi = new List<string>();
        var lette = new List<AnalisiLetta>();
        var perCodice = new Dictionary<string, (AnalisiAziendale Analisi, List<string> Errori)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in testate)
            foreach (var (riga, v) in t.Righe)
            {
                var errori = new List<string>();
                var a = new AnalisiAziendale
                {
                    Id = Guid.Empty,
                    Codice = t.Valore(v, "codice"),
                    Descrizione = t.Valore(v, "descrizione") ?? "",
                    Unita = t.Valore(v, "unita", "um", "unitadimisura"),
                    Fonte = t.Valore(v, "fonte"),
                    Note = t.Valore(v, "note"),
                };
                if (a.Codice is null && a.Descrizione.Length == 0) continue;
                if (a.Codice is null) errori.Add("Codice mancante: serve a riconoscere l'analisi nelle importazioni successive.");
                if (a.Descrizione.Length == 0) errori.Add("Descrizione mancante.");
                else if (a.Descrizione.Length > LunghezzaDescrizione) errori.Add($"Descrizione più lunga di {LunghezzaDescrizione} caratteri.");
                if (UnitaMisura.Normalizza(a.Unita) is { Length: > LunghezzaUnita }) errori.Add($"Unità di misura più lunga di {LunghezzaUnita} caratteri.");
                if (Stato(t.Valore(v, "stato")) is { } stato) a.Stato = stato;
                else errori.Add($"Stato «{t.Valore(v, "stato")}» non riconosciuto: usa Bozza, Confermata o Archiviata.");
                if (a.Codice is not null && !perCodice.TryAdd(a.Codice, (a, errori)))
                    errori.Add($"Codice {a.Codice} ripetuto nel file: si importa solo la prima analisi.");
                lette.Add(new AnalisiLetta(t.File, riga, a, errori));
            }

        var orfane = 0;
        var ordini = new Dictionary<AnalisiAziendale, List<(decimal Ordine, int Riga, RigaAnalisiAziendale R)>>();
        foreach (var t in righe)
            foreach (var (riga, v) in t.Righe)
            {
                var codice = t.Valore(v, "codiceanalisi");
                if (codice is null) continue;
                if (!perCodice.TryGetValue(codice, out var dest)) { orfane++; continue; }
                var r = new RigaAnalisiAziendale
                {
                    Descrizione = t.Valore(v, "descrizione") ?? "",
                    Unita = t.Valore(v, "unita", "um", "unitadimisura"),
                    Fonte = t.Valore(v, "fonte"),
                    Note = t.Valore(v, "note"),
                };
                var dove = $"riga {riga} di {t.File}";
                if (Categoria(t.Valore(v, "categorianumero"), t.Valore(v, "categoria")) is { } categoria) r.Categoria = categoria;
                else dest.Errori.Add($"Categoria «{t.Valore(v, "categoria", "categorianumero")}» non riconosciuta ({dove}).");
                if (r.Descrizione.Length == 0) dest.Errori.Add($"Risorsa senza descrizione ({dove}).");
                else if (r.Descrizione.Length > LunghezzaDescrizione) dest.Errori.Add($"Descrizione della risorsa più lunga di {LunghezzaDescrizione} caratteri ({dove}).");
                if (UnitaMisura.Normalizza(r.Unita) is { Length: > LunghezzaUnita }) dest.Errori.Add($"Unità della risorsa più lunga di {LunghezzaUnita} caratteri ({dove}).");
                r.Fabbisogno = NumeroObbligatorio(t.Valore(v, "fabbisogno", "quantita", "incidenza"), "fabbisogno", dove, dest.Errori);
                r.Prezzo = NumeroObbligatorio(t.Valore(v, "prezzo", "prezzounitario"), "prezzo", dove, dest.Errori);
                r.SfridoPercentuale = t.Valore(v, "sfridopercentuale", "sfrido") is { } sfrido
                    ? NumeroObbligatorio(sfrido, "sfrido", dove, dest.Errori) : 0;
                var ordine = Numero(t.Valore(v, "ordine")) ?? decimal.MaxValue;
                if (!ordini.TryGetValue(dest.Analisi, out var elenco)) ordini[dest.Analisi] = elenco = [];
                elenco.Add((ordine, riga, r));
            }
        foreach (var (a, elenco) in ordini)
        {
            var n = 0;
            foreach (var (_, _, r) in elenco.OrderBy(x => x.Ordine).ThenBy(x => x.Riga))
            {
                r.Ordine = ++n;
                a.Righe.Add(r);
            }
        }
        foreach (var l in lette.Where(l => l.Analisi.Stato == StatoAnalisi.Confermata && l.Analisi.Righe.Count == 0))
            l.Errori.Add("Analisi confermata senza risorse.");

        if (orfane > 0)
            avvisi.Add($"{orfane} righe di risorse si riferiscono a codici di analisi che non sono nel file e sono state ignorate.");
        return new FileAnalisiLetto(lette, avvisi, escluse);
    }

    private static IEnumerable<Tabella> Tabelle(string nome, Stream contenuto)
    {
        switch (Path.GetExtension(nome).ToLowerInvariant())
        {
            case ".xlsx" or ".xlsm":
                foreach (var f in LettoreXlsx.Leggi(contenuto))
                {
                    var righe = new List<(int, IReadOnlyList<string>)>();
                    for (var r = 1; r <= f.UltimaRiga; r++)
                    {
                        var valori = new string[f.UltimaColonna];
                        for (var c = 1; c <= f.UltimaColonna; c++)
                            // Excel memorizza i numeri in virgola mobile (86.26 → 86.26000000000001): si riporta la cifra scritta.
                            valori[c - 1] = f.Cella(r, c)?.Valore is decimal d
                                ? ((double)d).ToString(CultureInfo.InvariantCulture)
                                : f.Testo(r, c) ?? "";
                        righe.Add((r, valori));
                    }
                    if (DaRighe($"{nome} › {f.Nome}", righe) is { } t) yield return t;
                }
                break;
            case ".csv" or ".txt":
                var csv = ImportazioneAnagraficheService.Leggi(contenuto, nome)
                    .Select((v, i) => (i + 1, (IReadOnlyList<string>)v)).ToList();
                if (DaRighe(nome, csv) is { } tc) yield return tc;
                break;
            default:
                throw new OperazioneNonValidaException($"Formato di «{nome}» non riconosciuto: usa il file Excel (.xlsx) o i CSV del tracciato.");
        }
    }

    /// <summary>La prima riga non vuota contiene le intestazioni; le successive non vuote sono i dati.</summary>
    private static Tabella? DaRighe(string file, IReadOnlyList<(int Riga, IReadOnlyList<string> Valori)> righe)
    {
        var piene = righe.Where(r => r.Valori.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        if (piene.Count == 0) return null;
        return new Tabella(file, piene[0].Valori, piene.Skip(1).ToList());
    }

    private static string Chiave(string intestazione)
    {
        var s = intestazione.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }

    private static StatoAnalisi? Stato(string? testo) => Chiave(testo ?? "") switch
    {
        "" or "bozza" or "0" => StatoAnalisi.Bozza,
        "confermata" or "1" => StatoAnalisi.Confermata,
        "archiviata" or "2" => StatoAnalisi.Archiviata,
        _ => null,
    };

    private static CategoriaRisorsa? Categoria(string? numero, string? nome)
    {
        if (Numero(numero) is { } n && Enum.IsDefined(typeof(CategoriaRisorsa), (int)n) && n == decimal.Truncate(n))
            return (CategoriaRisorsa)(int)n;
        return Chiave(nome ?? "") switch
        {
            "materiali" or "materiale" => CategoriaRisorsa.Materiali,
            "manodopera" => CategoriaRisorsa.Manodopera,
            "attrezzature" or "noli" or "nolo" or "mezzi" => CategoriaRisorsa.Attrezzature,
            "subappalti" or "subappalto" => CategoriaRisorsa.Subappalti,
            "trasportiesmaltimenti" or "trasporti" => CategoriaRisorsa.TrasportiESmaltimenti,
            "altricosti" or "altro" => CategoriaRisorsa.AltriCosti,
            _ => null,
        };
    }

    /// <summary>Numero con il punto o la virgola come separatore decimale (i CSV del tracciato usano il punto).</summary>
    internal static decimal? Numero(string? testo)
    {
        if (string.IsNullOrWhiteSpace(testo)) return null;
        var t = testo.Trim().Replace(" ", "").Replace("€", "");
        if (t.Contains(',') && !t.Contains('.')) t = t.Replace(',', '.');
        else if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static decimal NumeroObbligatorio(string? testo, string campo, string dove, List<string> errori)
    {
        if (Numero(testo) is not { } d)
        {
            errori.Add(testo is null ? $"Valore di {campo} mancante ({dove})." : $"Valore di {campo} «{testo}» non numerico ({dove}).");
            return 0;
        }
        if (d < 0) errori.Add($"Valore di {campo} negativo ({dove}): Corico non accetta valori negativi.");
        return d;
    }
}
