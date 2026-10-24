using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Corico.Core.Backup;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public enum TipoAnagrafica { Clienti, Fornitori }

public enum EsitoRigaAnagrafica { Nuovo, GiaPresente, RipetutoNelFile, Scartato }

/// <summary>Una riga del file, con i soli campi che esistono in Corico.</summary>
public sealed class RigaAnagrafica
{
    public int Riga { get; init; }
    public string? Ditta { get; init; }
    public string Denominazione { get; init; } = "";
    public string? PartitaIva { get; init; }
    public string? CodiceFiscale { get; init; }
    public string? Indirizzo { get; init; }
    public string? Comune { get; init; }
    public string? Provincia { get; init; }
    public string? Email { get; init; }
    public string? Telefono { get; init; }
    public string? Categorie { get; init; }

    public EsitoRigaAnagrafica Esito { get; set; }
    public string? Motivo { get; set; }

    /// <summary>Proposta: si importano le righe nuove; l'utente può cambiare la scelta riga per riga.</summary>
    public bool Importa { get; set; }
}

public sealed record AnteprimaAnagrafiche(
    TipoAnagrafica Tipo,
    string NomeFile,
    IReadOnlyList<RigaAnagrafica> Righe,
    IReadOnlyList<string> ColonneUsate,
    IReadOnlyList<string> ColonneIgnorate,
    IReadOnlyList<string> Ditte)
{
    public int Conta(EsitoRigaAnagrafica esito) => Righe.Count(r => r.Esito == esito);
}

/// <summary>
/// Importazione di clienti e fornitori da Excel (.xlsx) o CSV esportati dal gestionale: si leggono solo i campi
/// presenti in Corico (gli altri, es. CAP, fax, IBAN, si ignorano), con anteprima, controllo dei duplicati per
/// partita IVA, codice fiscale e denominazione, backup prima dell'inserimento.
/// </summary>
public sealed class ImportazioneAnagraficheService(IDbContextFactory<CoreDbContext> fabbrica, BackupService backup)
{
    public const int MassimoRighe = 20_000;

    public async Task<AnteprimaAnagrafiche> AnteprimaAsync(TipoAnagrafica tipo, Stream contenuto, string nomeFile, CancellationToken ct = default)
    {
        var tabella = Leggi(contenuto, nomeFile);
        if (tabella.Count < 2) throw new OperazioneNonValidaException("Il file non contiene righe da importare.");
        if (tabella.Count - 1 > MassimoRighe) throw new OperazioneNonValidaException($"Il file supera le {MassimoRighe} righe.");

        var intestazioni = tabella[0];
        var mappa = Mappa(intestazioni, tipo);
        if (!mappa.ContainsKey(Campo.Denominazione))
            throw new OperazioneNonValidaException(
                "Non trovo la colonna con la ragione sociale (es. «Ragione sociale», «Denominazione»). Controlla la prima riga del file.");

        var righe = new List<RigaAnagrafica>();
        for (var i = 1; i < tabella.Count; i++)
        {
            var r = tabella[i];
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            string? V(Campo c) => mappa.TryGetValue(c, out var col) && col < r.Count && !string.IsNullOrWhiteSpace(r[col]) ? r[col].Trim() : null;
            righe.Add(new RigaAnagrafica
            {
                Riga = i + 1,
                // Il gestionale può esportare le anagrafiche di più imprese del gruppo nello stesso file.
                Ditta = V(Campo.Ditta) is { } d ? (V(Campo.NomeDitta) is { } nd ? $"{d} – {nd}" : d) : null,
                Denominazione = Taglia(V(Campo.Denominazione), 300) ?? "",
                PartitaIva = PartitaIva(V(Campo.PartitaIva)),
                CodiceFiscale = CodiceFiscale(V(Campo.CodiceFiscale)),
                Indirizzo = Taglia(V(Campo.Indirizzo), 300),
                Comune = Taglia(V(Campo.Comune), 150),
                Provincia = Taglia(V(Campo.Provincia)?.ToUpperInvariant(), 5),
                Email = Taglia(V(Campo.Email)?.ToLowerInvariant(), 200),
                Telefono = Taglia(Telefono(V(Campo.Telefono)), 50),
                Categorie = Taglia(V(Campo.Categorie), 300),
            });
        }

        await ClassificaAsync(tipo, righe, ct);
        var usate = mappa.Where(m => m.Key is not (Campo.Ditta or Campo.NomeDitta)).Select(m => $"{intestazioni[m.Value]} → {Etichetta(m.Key)}").ToList();
        var ignorate = intestazioni.Where((h, i) => !mappa.ContainsValue(i) && !string.IsNullOrWhiteSpace(h)).ToList();
        var ditte = righe.Select(r => r.Ditta).OfType<string>().Distinct().Order().ToList();
        return new AnteprimaAnagrafiche(tipo, nomeFile, righe, usate, ignorate, ditte.Count > 1 ? ditte : []);
    }

    /// <summary>Inserisce le righe scelte. Restituisce il numero di anagrafiche create.</summary>
    public async Task<int> ConfermaAsync(AnteprimaAnagrafiche anteprima, CancellationToken ct = default)
    {
        var scelte = anteprima.Righe.Where(r => r.Importa && r.Esito != EsitoRigaAnagrafica.Scartato).ToList();
        if (scelte.Count == 0) throw new OperazioneNonValidaException("Nessuna riga selezionata da importare.");

        var esitoBackup = await backup.EseguiBackupLocaleAsync(MotivoBackup.PrimaDiImportazione, ct);
        if (!esitoBackup.Riuscito)
            throw new OperazioneNonValidaException("Importazione annullata: il backup preventivo non è riuscito. " + esitoBackup.Messaggio);

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        db.InserimentiSoloInRiepilogo = true;
        foreach (var r in scelte)
        {
            if (anteprima.Tipo == TipoAnagrafica.Clienti)
                db.Clienti.Add(new Cliente
                {
                    Denominazione = r.Denominazione, PartitaIva = r.PartitaIva, CodiceFiscale = r.CodiceFiscale, Indirizzo = r.Indirizzo,
                    Comune = r.Comune, Provincia = r.Provincia, Email = r.Email, Telefono = r.Telefono,
                });
            else
                db.Fornitori.Add(new Fornitore
                {
                    Denominazione = r.Denominazione, PartitaIva = r.PartitaIva, CodiceFiscale = r.CodiceFiscale, Indirizzo = r.Indirizzo,
                    Comune = r.Comune, Provincia = r.Provincia, Email = r.Email, Telefono = r.Telefono, Categorie = r.Categorie,
                });
        }
        var nome = anteprima.Tipo == TipoAnagrafica.Clienti ? "Cliente" : "Fornitore";
        db.RegistraOperazione("Importazione", nome, null, null,
            $"{scelte.Count} {(anteprima.Tipo == TipoAnagrafica.Clienti ? "clienti" : "fornitori")} importati da «{anteprima.NomeFile}»");
        await db.SaveChangesAsync(ct);
        return scelte.Count;
    }

    // --- Classificazione e duplicati ---

    private async Task ClassificaAsync(TipoAnagrafica tipo, List<RigaAnagrafica> righe, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var esistenti = tipo == TipoAnagrafica.Clienti
            ? await db.Clienti.AsNoTracking().Select(c => new { c.Denominazione, c.PartitaIva, c.CodiceFiscale }).ToListAsync(ct)
            : await db.Fornitori.AsNoTracking().Select(f => new { f.Denominazione, f.PartitaIva, f.CodiceFiscale }).ToListAsync(ct);

        var codici = new Dictionary<string, string>();   // P.IVA o C.F. normalizzati → denominazione
        var nomi = new Dictionary<string, string>();
        foreach (var e in esistenti)
        {
            Registra(codici, "P", ControlloDuplicati.NormalizzaCodice(e.PartitaIva), e.Denominazione);
            Registra(codici, "C", ControlloDuplicati.NormalizzaCodice(e.CodiceFiscale), e.Denominazione);
            Registra(nomi, "", ControlloDuplicati.NormalizzaNome(e.Denominazione), e.Denominazione);
        }

        var codiciFile = new Dictionary<string, int>();
        var nomiFile = new Dictionary<string, int>();
        foreach (var r in righe)
        {
            var piva = ControlloDuplicati.NormalizzaCodice(r.PartitaIva);
            var cf = ControlloDuplicati.NormalizzaCodice(r.CodiceFiscale);
            var nome = ControlloDuplicati.NormalizzaNome(r.Denominazione);

            if (string.IsNullOrWhiteSpace(r.Denominazione))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.Scartato, "Ragione sociale mancante");
            else if (piva is not null && codici.TryGetValue("P" + piva, out var p))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.GiaPresente, $"Stessa partita IVA di «{p}»");
            else if (cf is not null && codici.TryGetValue("C" + cf, out var c))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.GiaPresente, $"Stesso codice fiscale di «{c}»");
            else if (nome.Length > 0 && nomi.TryGetValue(nome, out var n))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.GiaPresente, $"Stessa ragione sociale di «{n}»");
            else if (piva is not null && codiciFile.TryGetValue("P" + piva, out var rp))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.RipetutoNelFile, $"Stessa partita IVA della riga {rp}");
            else if (cf is not null && codiciFile.TryGetValue("C" + cf, out var rc))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.RipetutoNelFile, $"Stesso codice fiscale della riga {rc}");
            else if (nome.Length > 0 && piva is null && cf is null && nomiFile.TryGetValue(nome, out var rn))
                (r.Esito, r.Motivo) = (EsitoRigaAnagrafica.RipetutoNelFile, $"Stessa ragione sociale della riga {rn}");
            else
                r.Esito = EsitoRigaAnagrafica.Nuovo;

            r.Importa = r.Esito == EsitoRigaAnagrafica.Nuovo;
            if (r.Esito == EsitoRigaAnagrafica.Scartato) continue;
            if (piva is not null) codiciFile.TryAdd("P" + piva, r.Riga);
            if (cf is not null) codiciFile.TryAdd("C" + cf, r.Riga);
            if (nome.Length > 0) nomiFile.TryAdd(nome, r.Riga);
        }
    }

    private static void Registra(Dictionary<string, string> d, string prefisso, string? chiave, string valore)
    {
        if (!string.IsNullOrEmpty(chiave)) d.TryAdd(prefisso + chiave, valore);
    }

    // --- Normalizzazione dei campi ---

    /// <summary>Le esportazioni in Excel perdono gli zeri iniziali: una partita IVA numerica si riporta a 11 cifre.</summary>
    internal static string? PartitaIva(string? valore)
    {
        if (valore is null) return null;
        var s = valore.Replace(" ", "").ToUpperInvariant();
        if (s.StartsWith("IT") && s.Length > 2 && s[2..].All(char.IsDigit)) s = s[2..];
        if (s.All(char.IsDigit) && s.Length is >= 1 and < 11) s = s.PadLeft(11, '0');
        return Taglia(s, 20);
    }

    /// <summary>Codice fiscale di persona (16 caratteri) o numerico di società, riportato a 11 cifre.</summary>
    internal static string? CodiceFiscale(string? valore)
    {
        if (valore is null) return null;
        var s = valore.Replace(" ", "").ToUpperInvariant();
        if (s.All(char.IsDigit) && s.Length is >= 1 and < 11) s = s.PadLeft(11, '0');
        return Taglia(s, 20);
    }

    /// <summary>
    /// Un numero fisso italiano inizia sempre con 0, che le esportazioni in Excel perdono (es. 932762044 → 0932762044);
    /// i cellulari iniziano con 3 e restano come sono.
    /// </summary>
    internal static string? Telefono(string? valore)
    {
        if (valore is null) return null;
        var s = valore.Trim();
        return s.All(char.IsDigit) && s.Length is >= 5 and <= 10 && s[0] is not ('0' or '3') ? "0" + s : s;
    }

    private static string? Taglia(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    // --- Riconoscimento delle colonne ---

    private enum Campo { Ditta, NomeDitta, Denominazione, PartitaIva, CodiceFiscale, Indirizzo, Comune, Provincia, Email, Telefono, Categorie }

    private static string Etichetta(Campo c) => c switch
    {
        Campo.Denominazione => "Denominazione",
        Campo.PartitaIva => "Partita IVA",
        Campo.CodiceFiscale => "Codice fiscale",
        Campo.Indirizzo => "Indirizzo",
        Campo.Comune => "Comune",
        Campo.Provincia => "Provincia",
        Campo.Email => "E-mail",
        Campo.Telefono => "Telefono",
        Campo.Categorie => "Categorie",
        _ => c.ToString(),
    };

    private static Dictionary<Campo, int> Mappa(IReadOnlyList<string> intestazioni, TipoAnagrafica tipo)
    {
        var mappa = new Dictionary<Campo, int>();
        for (var i = 0; i < intestazioni.Count; i++)
        {
            var h = Chiave(intestazioni[i]);
            Campo? campo = h switch
            {
                "ditta" or "codiceditta" => Campo.Ditta,
                _ when h.Contains("ditta") && (h.Contains("ragsoc") || h.Contains("ragionesociale")) => Campo.NomeDitta,
                _ when h.Contains("ditta") => null,                  // altri dati della propria impresa
                _ when h.Contains("estera") => null,                 // partita IVA estera
                _ when h.Contains("ragionesociale") || h.StartsWith("denominazione") || h == "nominativo" || h == "ragsoc" => Campo.Denominazione,
                _ when h.Contains("partitaiva") || h is "piva" or "pivacodicefiscale" => Campo.PartitaIva,
                _ when h.Contains("codicefiscale") || h == "cf" => Campo.CodiceFiscale,
                _ when h.StartsWith("indirizzo") || h == "via" || h == "sede" => Campo.Indirizzo,
                _ when h.StartsWith("citta") || h.StartsWith("comune") || h.StartsWith("localita") => Campo.Comune,
                _ when h.StartsWith("provincia") || h is "prov" or "pr" => Campo.Provincia,
                _ when h.Contains("mail") || h == "pec" => Campo.Email,
                _ when h.StartsWith("telefono") || h is "tel" or "cellulare" => Campo.Telefono,
                _ when tipo == TipoAnagrafica.Fornitori && h.StartsWith("categori") => Campo.Categorie,
                _ => null,
            };
            if (campo is { } c) mappa.TryAdd(c, i);
        }
        return mappa;
    }

    private static string Chiave(string intestazione)
    {
        var s = intestazione.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        s = new string(s.Where(char.IsLetterOrDigit).ToArray());
        // «attivita» nelle intestazioni del gestionale (es. «Citta' (att.)») non cambia il significato.
        return s.Replace("attivita", "").Replace("att", "");
    }

    // --- Lettura del file ---

    /// <summary>Righe di un file Excel (primo foglio) o CSV, come testo; usata anche da altre importazioni tabellari.</summary>
    public static List<List<string>> Leggi(Stream contenuto, string nomeFile)
    {
        var estensione = Path.GetExtension(nomeFile).ToLowerInvariant();
        return estensione switch
        {
            ".xlsx" or ".xlsm" => LeggiExcel(contenuto),
            ".csv" or ".txt" => LeggiCsv(contenuto),
            ".xls" => throw new OperazioneNonValidaException(
                "Il formato .xls (Excel 97-2003) non è supportato: apri il file in Excel e salvalo come «Cartella di lavoro Excel (.xlsx)» oppure come CSV."),
            _ => throw new OperazioneNonValidaException("Formato non riconosciuto: usa un file Excel (.xlsx) o CSV."),
        };
    }

    private static List<List<string>> LeggiExcel(Stream contenuto)
    {
        using var cartella = new XLWorkbook(contenuto);
        var foglio = cartella.Worksheets.First();
        var area = foglio.RangeUsed();
        if (area is null) return [];
        var colonne = area.ColumnCount();
        var risultato = new List<List<string>>();
        foreach (var riga in area.Rows())
        {
            var valori = new List<string>(colonne);
            for (var c = 1; c <= colonne; c++)
            {
                var cella = riga.Cell(c);
                valori.Add(cella.DataType == XLDataType.Number
                    ? cella.GetDouble().ToString("0.##########", CultureInfo.InvariantCulture)
                    : cella.GetFormattedString());
            }
            risultato.Add(valori);
        }
        return risultato;
    }

    /// <summary>CSV con separatore «;», «,» o tabulazione; UTF-8 oppure Windows-1252 (tipico dei gestionali).</summary>
    private static List<List<string>> LeggiCsv(Stream contenuto)
    {
        using var ms = new MemoryStream();
        contenuto.CopyTo(ms);
        var byteFile = ms.ToArray();
        string testo;
        try
        {
            testo = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(byteFile);
        }
        catch (DecoderFallbackException)
        {
            testo = CodePagesEncodingProvider.Instance.GetEncoding(1252)?.GetString(byteFile) ?? Encoding.Latin1.GetString(byteFile);
        }
        testo = testo.TrimStart('﻿');

        var primaRiga = testo.Split('\n', 2)[0];
        var separatore = new[] { ';', '\t', ',' }.OrderByDescending(s => primaRiga.Count(ch => ch == s)).First();

        var righe = new List<List<string>>();
        var campi = new List<string>();
        var campo = new StringBuilder();
        var traVirgolette = false;
        for (var i = 0; i < testo.Length; i++)
        {
            var ch = testo[i];
            if (traVirgolette)
            {
                if (ch == '"' && i + 1 < testo.Length && testo[i + 1] == '"') { campo.Append('"'); i++; }
                else if (ch == '"') traVirgolette = false;
                else campo.Append(ch);
            }
            else if (ch == '"' && campo.Length == 0) traVirgolette = true;
            else if (ch == separatore) { campi.Add(campo.ToString()); campo.Clear(); }
            else if (ch == '\n')
            {
                campi.Add(campo.ToString().TrimEnd('\r')); campo.Clear();
                righe.Add(campi); campi = [];
            }
            else campo.Append(ch);
        }
        if (campo.Length > 0 || campi.Count > 0) { campi.Add(campo.ToString().TrimEnd('\r')); righe.Add(campi); }
        return righe;
    }
}
