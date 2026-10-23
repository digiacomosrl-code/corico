using System.Globalization;
using System.Text;
using System.Text.Json;
using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>
/// Testi e controlli della proposta WBS fatta dal servizio AI esterno (Modulo 3): istruzioni, contenuto inviato
/// (visibile nell'anteprima prima dell'invio), schema della risposta e sua lettura in una <see cref="PropostaWbs"/>.
/// L'AI legge tutte le voci, riconosce la tipologia dei lavori, ordina le lavorazioni secondo le tecniche costruttive
/// e la sequenza cronologica di realizzazione, numera la WBS in quell'ordine e vi associa ogni voce.
/// Nulla viene salvato: la proposta si applica solo dopo la conferma dell'utente.
/// </summary>
public static class WbsAi
{
    public const string Funzione = "WBS";

    /// <summary>Lunghezza massima della descrizione di una voce inviata all'AI: basta a riconoscere la lavorazione.</summary>
    public const int LunghezzaDescrizione = 350;

    public const string Istruzioni = """
        Sei un tecnico esperto di cantieri edili e impiantistici italiani. Devi proporre la WBS (Work Breakdown Structure)
        di una commessa partendo dalle voci del computo metrico estimativo.

        Procedi così:
        1. Leggi tutte le voci e stabilisci la tipologia dei lavori (es. nuova costruzione in c.a., ristrutturazione edilizia,
           efficientamento energetico, manutenzione straordinaria, opere stradali, impianti...).
        2. Individua le lavorazioni e le loro priorità secondo le tecniche costruttive di quella tipologia: cosa deve
           essere eseguito prima perché le altre lavorazioni possano partire (es. allestimento del cantiere, demolizioni e
           rimozioni, scavi, strutture, chiusure e impermeabilizzazioni, tracce e impianti sottotraccia, intonaci e massetti,
           pavimenti e rivestimenti, serramenti, finiture, impianti a vista e allacci, sistemazioni esterne, smobilizzo).
        3. Stabilisci l'ordine cronologico di realizzazione e numera la WBS in quell'ordine: il codice 1 è la prima fase
           da realizzare, il 2 la seconda e così via; dentro ogni fase i sottolivelli (1.1, 1.2...) seguono a loro volta
           l'ordine di esecuzione. Usa al massimo tre livelli e solo codici numerici separati da punti.
        4. Associa ogni voce del computo a un solo elemento della WBS, il più dettagliato che la descrive. Ogni voce deve
           comparire una e una sola volta. Gli oneri della sicurezza vanno nella fase in cui servono o, se generali,
           nell'allestimento del cantiere.

        Le descrizioni della WBS sono brevi (al massimo 60 caratteri), in italiano, con la maiuscola iniziale.
        Per ogni elemento indica in «motivo», in una frase, perché si trova in quella posizione della sequenza.
        In «criteri» spiega in poche righe la sequenza scelta. Non inventare voci e non cambiare i numeri delle voci.
        """;

    public const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["tipologia", "criteri", "elementi"],
          "properties": {
            "tipologia": { "type": "string", "description": "Tipologia dei lavori riconosciuta" },
            "criteri": { "type": "string", "description": "Spiegazione breve della sequenza cronologica scelta" },
            "elementi": {
              "type": "array",
              "description": "Elementi della WBS in ordine cronologico di realizzazione",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["codice", "descrizione", "motivo", "voci"],
                "properties": {
                  "codice": { "type": "string", "description": "Codice gerarchico numerico, es. 2 o 2.1 o 2.1.3" },
                  "descrizione": { "type": "string" },
                  "motivo": { "type": "string", "description": "Perché questa lavorazione si trova in questa posizione" },
                  "voci": { "type": "array", "items": { "type": "integer" }, "description": "Numeri delle voci associate" }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// Testo inviato all'AI: una riga per voce con numero progressivo, tariffa, categorie, descrizione accorciata e importo.
    /// I numeri progressivi (non gli identificativi interni) servono a ricollegare la risposta alle voci.
    /// </summary>
    public static string Contenuto(ContestoPropostaWbs contesto)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Voci del computo: {contesto.Voci.Count}. Importi in euro, al lordo del ribasso.");
        sb.AppendLine("N. | Tariffa | Categoria | Descrizione | Importo");
        var n = 0;
        foreach (var v in Ordinate(contesto))
        {
            n++;
            var categoria = string.Join(" > ", new[] { v.SuperCategoria, v.Categoria }
                .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
            sb.Append(n).Append(" | ").Append(Pulisci(v.Tariffa)).Append(" | ").Append(Pulisci(categoria)).Append(" | ")
              .Append(Accorcia(v.Descrizione)).Append(" | ")
              .AppendLine(v.RicavoLordo.ToString("0.00", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Legge la risposta dell'AI e la controlla: codici validi e non ripetuti, livelli superiori presenti, ogni voce
    /// associata una sola volta. Gli scostamenti diventano note, mai errori silenziosi. La proposta contiene
    /// l'intera struttura in ordine cronologico e le assegnazioni di tutte le voci del computo.
    /// </summary>
    public static PropostaWbs Interpreta(string? testo, ContestoPropostaWbs contesto)
    {
        var note = new List<string>();
        RispostaWbsAi? risposta;
        try
        {
            risposta = string.IsNullOrWhiteSpace(testo) ? null
                : JsonSerializer.Deserialize<RispostaWbsAi>(EstraiJson(testo), OpzioniJson);
        }
        catch (JsonException)
        {
            risposta = null;
        }
        if (risposta?.Elementi is not { Count: > 0 })
        {
            note.Add("La risposta dell'AI non contiene una WBS leggibile: nessuna proposta. Puoi riprovare o usare la proposta senza AI.");
            return new PropostaWbs([], [], note);
        }

        if (!string.IsNullOrWhiteSpace(risposta.Tipologia)) note.Add($"Tipologia dei lavori: {risposta.Tipologia.Trim()}.");
        if (!string.IsNullOrWhiteSpace(risposta.Criteri)) note.Add($"Sequenza: {risposta.Criteri.Trim()}");

        var voci = Ordinate(contesto).ToList();
        var elementi = new List<ElementoWbsDati>();
        var motivi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var vocePerNumero = new Dictionary<int, string>();
        var scartati = new List<string>();
        var ripetute = new SortedSet<int>();
        var inesistenti = new SortedSet<int>();
        foreach (var e in risposta.Elementi)
        {
            var codice = CodiciWbs.Normalizza(e.Codice);
            if (codice is null || codice.Length > CodiciWbs.LunghezzaMassima || !codice.All(c => char.IsDigit(c) || c == '.')
                || motivi.ContainsKey(codice))
            {
                scartati.Add(string.IsNullOrWhiteSpace(e.Codice) ? "(senza codice)" : e.Codice.Trim());
                continue;
            }
            var descrizione = string.IsNullOrWhiteSpace(e.Descrizione) ? $"WBS {codice}" : e.Descrizione.Trim();
            elementi.Add(new(codice, descrizione.Length > 300 ? descrizione[..300] : descrizione));
            motivi[codice] = string.IsNullOrWhiteSpace(e.Motivo) ? descrizione : e.Motivo.Trim();
            foreach (var n in e.Voci ?? [])
            {
                if (n < 1 || n > voci.Count) inesistenti.Add(n);
                else if (!vocePerNumero.TryAdd(n, codice)) ripetute.Add(n);
            }
        }
        if (scartati.Count > 0)
            note.Add($"Elementi scartati perché con codice non valido o ripetuto: {string.Join(", ", scartati)}.");
        if (ripetute.Count > 0)
            note.Add($"Voci indicate dall'AI in più elementi (tenuto il primo): {Elenco(ripetute)}.");
        if (inesistenti.Count > 0)
            note.Add($"Numeri di voce inesistenti, ignorati: {Elenco(inesistenti)}.");

        // Livelli superiori mancanti (es. 2.1 senza 2): li si aggiunge, perché filtri e totali lavorano per gerarchia.
        var codici = elementi.Select(e => e.Codice).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in elementi.ToList())
        {
            for (var p = CodiciWbs.Padre(e.Codice); p is not null; p = CodiciWbs.Padre(p))
            {
                if (!codici.Add(p)) continue;
                elementi.Add(new(p, $"WBS {p} (da descrivere)"));
                motivi[p] = "Livello superiore aggiunto perché mancante nella risposta";
                note.Add($"Aggiunto il livello superiore {p}, da descrivere.");
            }
        }
        elementi.Sort((a, b) => CodiciWbs.Ordine.Compare(a.Codice, b.Codice));

        var assegnazioni = new List<AssegnazioneWbsProposta>();
        for (var i = 0; i < voci.Count; i++)
        {
            if (!vocePerNumero.TryGetValue(i + 1, out var codice)) continue;
            assegnazioni.Add(new(voci[i].Id, codice, FontePropostaWbs.Ai, motivi[codice]));
        }
        var senza = Enumerable.Range(1, voci.Count).Where(n => !vocePerNumero.ContainsKey(n)).ToList();
        if (senza.Count > 0)
            note.Add($"{senza.Count} voci non associate dall'AI (n. {Elenco(senza)}): assegnale a mano dalla tabella delle voci.");
        return new PropostaWbs(elementi, assegnazioni, note);
    }

    /// <summary>Le voci nell'ordine del computo: lo stesso usato per numerarle nel contenuto inviato.</summary>
    private static IEnumerable<VocePerWbs> Ordinate(ContestoPropostaWbs contesto) => contesto.Voci.OrderBy(v => v.Ordine);

    private static string Accorcia(string? testo)
    {
        var t = Pulisci(testo);
        return t.Length <= LunghezzaDescrizione ? t : t[..LunghezzaDescrizione].TrimEnd() + "…";
    }

    /// <summary>Una sola riga senza separatori di colonna, perché la tabella resti leggibile.</summary>
    private static string Pulisci(string? testo) =>
        string.IsNullOrWhiteSpace(testo) ? "" : string.Join(' ', testo.Replace('|', '/').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Elenco(IEnumerable<int> numeri)
    {
        var l = numeri.ToList();
        return l.Count <= 20 ? string.Join(", ", l) : string.Join(", ", l.Take(20)) + $" e altre {l.Count - 20}";
    }

    /// <summary>Tollera il JSON racchiuso in un blocco di codice o preceduto da testo.</summary>
    private static string EstraiJson(string testo)
    {
        var inizio = testo.IndexOf('{');
        var fine = testo.LastIndexOf('}');
        return inizio >= 0 && fine > inizio ? testo[inizio..(fine + 1)] : testo;
    }

    private static readonly JsonSerializerOptions OpzioniJson = new() { PropertyNameCaseInsensitive = true };

    private sealed record RispostaWbsAi(string? Tipologia, string? Criteri, List<ElementoRispostaAi>? Elementi);
    private sealed record ElementoRispostaAi(string? Codice, string? Descrizione, string? Motivo, List<int>? Voci);
}
