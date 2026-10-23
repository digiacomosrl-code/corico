using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;
using Corico.Core.Calcolo;
using UglyToad.PdfPig;

namespace Corico.Modules.Preventivo.Importazione;

/// <summary>
/// Lettura dei computi metrici estimativi stampati in PDF (anche firmati digitalmente, PAdES o .p7m).
/// Si legge il testo riga per riga e si riconoscono le voci dallo schema comune delle stampe dei programmi di contabilità:
/// <list type="bullet">
/// <item>inizio voce: «N° Tariffa descrizione…», con il numero d'ordine progressivo;</item>
/// <item>righe di misura: terminano con due o più numeri;</item>
/// <item>fine voce: «Sommano [unità] [=] quantità [€] prezzo [€] importo».</item>
/// </list>
/// Fra una voce e l'altra, le righe di testo sono i capitoli (es. «OPERE INTERNE», «Opere di demolizione»).
/// Le intestazioni di pagina, i riporti e i riepiloghi finali si ignorano. Il totale del documento
/// («Totale €», «Sommano i lavori €») serve da controllo. Le scansioni (PDF senza testo) non sono leggibili qui.
/// </summary>
public static partial class LettoreComputoPdf
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public static LettoreComputoExcel.Risultato Leggi(Stream contenuto)
    {
        var pdf = ContenutoPdf(contenuto);
        List<string> righe;
        int pagine;
        try
        {
            using var documento = PdfDocument.Open(pdf);
            pagine = documento.NumberOfPages;
            righe = [];
            foreach (var pagina in documento.GetPages()) righe.AddRange(RighePagina(pagina));
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            throw new FormatException("Il file PDF non si riesce ad aprire (danneggiato o protetto da password): " + ex.Message);
        }
        if (righe.Count < pagine * 3)
            throw new FormatException(
                "Il PDF non contiene testo leggibile: è probabilmente una scansione. Per importarlo serve il file XPWE, " +
                "l'Excel del computo oppure un PDF stampato direttamente dal programma di contabilità.");
        return Analizza(righe);
    }

    /// <summary>
    /// Il PDF vero e proprio: un .p7m (firma CAdES, anche in base64) contiene il PDF firmato, che si estrae senza
    /// verificare la firma; un PDF firmato PAdES è già un PDF.
    /// </summary>
    public static byte[] ContenutoPdf(Stream contenuto)
    {
        using var ms = new MemoryStream();
        contenuto.CopyTo(ms);
        var dati = ms.ToArray();
        for (var livello = 0; livello < 3; livello++) // firme multiple: p7m dentro p7m
        {
            if (InizioPdf(dati)) return dati;
            dati = DaBase64(dati) ?? dati;
            if (InizioPdf(dati)) return dati;
            try
            {
                var firma = new SignedCms();
                firma.Decode(dati);
                dati = firma.ContentInfo.Content;
            }
            catch (CryptographicException)
            {
                break;
            }
        }
        throw new FormatException("Il file non è un PDF, né un PDF firmato digitalmente (.p7m).");
    }

    private static bool InizioPdf(byte[] dati) =>
        dati.AsSpan(0, Math.Min(dati.Length, 1024)).IndexOf("%PDF-"u8) >= 0;

    private static byte[]? DaBase64(byte[] dati)
    {
        try
        {
            var testo = Encoding.ASCII.GetString(dati);
            testo = Regex.Replace(testo, "-----[A-Z0-9 ]+-----", "");
            return Convert.FromBase64String(Regex.Replace(testo, @"\s", ""));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Righe di testo della pagina, dall'alto in basso. Dentro la riga le lettere restano nell'ordine in cui sono scritte nel PDF
    /// (nel testo giustificato le lettere si sovrappongono e l'ordine per posizione mescolerebbe le parole); i pezzi scritti
    /// in punti diversi della riga (colonne, numeri a destra) si rimettono in ordine da sinistra a destra.
    /// </summary>
    private static IEnumerable<string> RighePagina(UglyToad.PdfPig.Content.Page pagina)
    {
        var righe = new List<List<UglyToad.PdfPig.Content.Letter>>();
        foreach (var l in pagina.Letters)
        {
            if (l.Value.Length == 0) continue;
            var riga = righe.FindLast(r => Math.Abs(r[0].StartBaseLine.Y - l.StartBaseLine.Y) <= 2);
            if (riga is null) righe.Add([l]);
            else riga.Add(l);
        }
        foreach (var riga in righe.OrderByDescending(r => r[0].StartBaseLine.Y))
        {
            if (riga.All(l => string.IsNullOrWhiteSpace(l.Value))) continue;
            var pezzi = new List<(double Inizio, StringBuilder Testo)>();
            UglyToad.PdfPig.Content.Letter? precedente = null;
            foreach (var l in riga)
            {
                var corpo = Math.Max(l.PointSize, 4);
                var salto = precedente is null ? 0 : l.StartBaseLine.X - precedente.EndBaseLine.X;
                if (precedente is null || salto < -corpo || salto > corpo * 1.5)
                    pezzi.Add((l.StartBaseLine.X, new StringBuilder()));
                var testo = pezzi[^1].Testo;
                if (string.IsNullOrWhiteSpace(l.Value))
                {
                    if (testo.Length > 0 && testo[^1] != ' ') testo.Append(' ');
                    continue;
                }
                if (testo.Length > 0 && testo[^1] != ' ' && salto > corpo * 0.3) testo.Append(' ');
                testo.Append(l.Value);
                precedente = l;
            }
            yield return string.Join(' ', pezzi.OrderBy(p => p.Inizio).Select(p => p.Testo.ToString().Trim()).Where(t => t.Length > 0));
        }
    }

    // ---- Riconoscimento delle voci ----

    [GeneratedRegex(@"^(\d{1,5})\s+(\S+)(?:\s+(.*))?$")]
    private static partial Regex InizioVoce();

    [GeneratedRegex(@"^-?(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$")]
    private static partial Regex Numero();

    [GeneratedRegex(@"^(\d{1,4})\s+(\S.*)$")]
    private static partial Regex NumeroElenco();

    [GeneratedRegex(@"^(sommano|totale|importo|a riportare|riporto|pg\.|pag\.|pagina|n°|n\.|num\.|dimensioni|quadro riepilogativo|riepilogo)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RigaDiServizio();

    /// <summary>Interpretazione delle righe di testo del computo (separata dalla lettura del PDF per poterla verificare).</summary>
    public static LettoreComputoExcel.Risultato Analizza(IReadOnlyList<string> righe)
    {
        var letto = new ComputoLetto();
        decimal? totaleDocumento = null, totaleRiepilogo = null;
        decimal totaleImporti = 0;
        var differenzeImporto = 0;
        var senzaSommano = new List<string>();
        var ultimoNumero = 0;
        Voce? corrente = null;
        var inAttesa = new List<string>();      // righe di testo fra due voci: titoli dei capitoli
        var titoli = new List<string>();        // capitoli nell'ordine in cui si aprono
        var vociLette = new List<(Voce Voce, RigaSommano Sommano, int Capitolo)>();
        var riepilogo = new List<string>();     // righe del quadro riepilogativo, per ricostruire i livelli
        var nelRiepilogo = false;
        var tabella = false;                    // prima dell'intestazione della tabella c'è il frontespizio

        foreach (var grezza in righe)
        {
            var riga = Regex.Replace(grezza.Trim(), @"\s+", " ");
            if (riga.Length == 0) continue;
            if (IntestazioneOPagina(riga))
            {
                tabella |= IntestazioneTabella(riga);
                continue;
            }

            if (Regex.IsMatch(riga, @"^(Quadro riepilogativo|Riepilogo)\b", RegexOptions.IgnoreCase))
            {
                if (corrente is not null) { senzaSommano.Add(corrente.Tariffa); corrente = null; }
                nelRiepilogo = true;
                continue;
            }

            if (corrente is not null && Sommano(riga) is { } s)
            {
                vociLette.Add((corrente, s, titoli.Count - 1));
                totaleImporti += s.Importo;
                if (Math.Abs(Importi.Arrotonda(s.Quantita * s.Prezzo) - s.Importo) >= 0.01m) differenzeImporto++;
                corrente = null;
                continue;
            }

            // Totali: «Sommano € 16266,65» chiude un capitolo (la riga prima ne ripete il nome), «Totale € …» e
            // «Sommano i lavori € …» sono il totale del documento.
            if (corrente is null && Regex.Match(riga, @"^(Sommano|Totale)\b(.*?)€?\s*(-?[\d.]+,\d{2})$", RegexOptions.IgnoreCase) is { Success: true } t)
            {
                var etichetta = t.Groups[2].Value.Replace("€", "").Trim().ToLowerInvariant();
                var importo = ANumero(t.Groups[3].Value);
                var delDocumento = etichetta.Contains("lavori") || t.Groups[1].Value.Equals("Totale", StringComparison.OrdinalIgnoreCase) && etichetta.Length == 0;
                if (delDocumento && nelRiepilogo) totaleRiepilogo = importo;
                else if (delDocumento) totaleDocumento = importo;
                inAttesa.Clear();
                continue;
            }
            if (nelRiepilogo)
            {
                riepilogo.Add(riga);
                continue;
            }

            var inizio = InizioVoce().Match(riga);
            if (inizio.Success && int.TryParse(inizio.Groups[1].Value, out var numero) && !inizio.Groups[2].Value.Contains(',')
                && (corrente is null ? numero > ultimoNumero && numero <= ultimoNumero + 20 : numero == ultimoNumero + 1)
                && !RigaDiMisura(riga))
            {
                if (corrente is not null) senzaSommano.Add(corrente.Tariffa);
                titoli.AddRange(UnisciTitoli(inAttesa));
                inAttesa.Clear();
                var (tariffa, testo) = (inizio.Groups[2].Value, inizio.Groups[3].Value);
                // Tariffa scritta in due parti, «NP 5»: sigla e numero.
                if (Regex.Match(testo, @"^(\d+)\b\s*(.*)$") is { Success: true } seguito && Regex.IsMatch(tariffa, @"^[A-Za-z.]{1,4}$"))
                    (tariffa, testo) = ($"{tariffa} {seguito.Groups[1].Value}", seguito.Groups[2].Value);
                corrente = new Voce(numero, tariffa, testo);
                ultimoNumero = numero;
                continue;
            }

            if (corrente is null)
            {
                if (tabella && !riga.Equals("Computo", StringComparison.OrdinalIgnoreCase) && riga.Any(char.IsLetter))
                    inAttesa.Add(riga);
                continue;
            }

            // «SAL 1»: da qui in poi ci sono le misure.
            if (Regex.IsMatch(riga, @"^SAL \d+$", RegexOptions.IgnoreCase))
            {
                corrente.InMisure = true;
                continue;
            }
            if (corrente.InMisure || RigaDiMisura(riga))
            {
                corrente.InMisure = true;
                if (riga.StartsWith("Parziale", StringComparison.OrdinalIgnoreCase)) continue;
                var parti = riga.Split(' ');
                var numeri = parti.Reverse().TakeWhile(p => Numero().IsMatch(p)).Reverse().Select(ANumero).ToList();
                var testo = string.Join(' ', parti.Take(parti.Length - numeri.Count));
                corrente.Misure.Add(numeri.Count >= 2
                    ? new RigaMisuraLetta(testo.Length > 0 ? testo : null, null, null, null, null, numeri[^1], null)
                    : new RigaMisuraLetta(riga, null, null, null, null, null, null));
                continue;
            }

            // Alcune stampe mettono sulla riga del numero solo la tariffa, anche in due parti («1 NP. SGO.LOC»), e all'inizio
            // della riga seguente il numero dell'elenco prezzi («1 Sgombero, raccolta…»): il numero si toglie.
            if (corrente.Righe == 0 && !corrente.Descrizione.Any(char.IsLower) && corrente.Descrizione.Split(' ').Length <= 2
                && NumeroElenco().Match(riga) is { Success: true } ne)
            {
                corrente.Tariffa = (corrente.Tariffa + " " + corrente.Descrizione).Trim();
                corrente.Descrizione = "";
                riga = ne.Groups[2].Value;
            }
            corrente.Aggiungi(riga);
        }
        if (corrente is not null) senzaSommano.Add(corrente.Tariffa);

        if (vociLette.Count == 0)
            throw new FormatException(
                "Nel PDF non ho trovato voci di computo: servono righe «N° Tariffa descrizione» chiuse da «Sommano unità quantità prezzo importo».");

        // Capitoli: i livelli si ricavano dal quadro riepilogativo, se c'è; altrimenti sono tutti dello stesso livello.
        var livelli = LivelliDaRiepilogo(titoli, riepilogo) ?? titoli.Select(_ => (Livello: 1, Padre: -1)).ToList();
        string[] nomiLivello = ["SuperCategoria", "Categoria", "SubCategoria"];
        for (var i = 0; i < titoli.Count; i++)
            letto.Categorie.Add(new CategoriaLetta(nomiLivello[Math.Min(livelli[i].Livello, 3) - 1], $"C{i + 1}", null, titoli[i]));
        string? Antenato(int capitolo, int livello)
        {
            for (var c = capitolo; c >= 0; c = livelli[c].Padre)
                if (Math.Min(livelli[c].Livello, 3) == livello) return $"C{c + 1}";
            return null;
        }

        foreach (var (voce, s, capitolo) in vociLette)
        {
            var ep = new VoceElencoLetta($"E{letto.ElencoPrezzi.Count + 1}", voce.Tariffa, null, null,
                Abbrevia(voce.Descrizione), voce.Descrizione, s.Unita, UnitaMisura.Normalizza(s.Unita), s.Prezzo,
                null, null, null, null, null, null, null, []);
            letto.ElencoPrezzi.Add(ep);
            letto.Voci.Add(new VoceComputoLetta(voce.Numero, $"P{voce.Numero}", ep.Id, s.Quantita,
                capitolo < 0 ? null : Antenato(capitolo, 1), capitolo < 0 ? null : Antenato(capitolo, 2),
                capitolo < 0 ? null : Antenato(capitolo, 3), null, voce.Misure));
        }

        letto.Segnalazioni.Add(new(GravitaSegnalazione.Informazione,
            $"Computo PDF: {letto.Voci.Count} voci e {letto.Categorie.Count} capitoli. Le descrizioni sono quelle stampate nel PDF: conviene controllarne qualcuna."));
        if (senzaSommano.Count > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"{senzaSommano.Count} voci senza riga «Sommano» sono state ignorate: {string.Join(", ", senzaSommano.Take(5))}{(senzaSommano.Count > 5 ? " e altre" : "")}."));
        if (differenzeImporto > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"{differenzeImporto} voci hanno un importo diverso da quantità × prezzo arrotondato al centesimo."));
        var totale = totaleDocumento ?? totaleRiepilogo;
        if (totale is null)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione, "Nel PDF non ho trovato il totale dei lavori: il totale va verificato a mano."));
        else if (Math.Abs(totale.Value - totaleImporti) >= 0.01m)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"La somma degli importi delle voci lette ({totaleImporti.ToString("N2", It)} €) è diversa dal totale del documento " +
                $"({totale.Value.ToString("N2", It)} €): qualche voce potrebbe non essere stata riconosciuta."));
        else
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Informazione,
                $"Controllo superato: la somma degli importi delle voci è uguale al totale del documento ({totale.Value.ToString("N2", It)} €)."));

        return new LettoreComputoExcel.Risultato(letto, totale ?? totaleImporti, "Computo PDF");
    }

    /// <summary>Un titolo lungo va a capo («Opere provvisionali e di» / «sicurezza»): le righe che finiscono con una congiunzione o preposizione si uniscono alla seguente.</summary>
    private static IEnumerable<string> UnisciTitoli(List<string> righe)
    {
        var parte = "";
        foreach (var r in righe)
        {
            parte = (parte + " " + r).Trim();
            if (!Regex.IsMatch(parte, @"\b(e|ed|di|del|della|delle|dei|degli|per|con|in|a|da|su|tra|fra)$", RegexOptions.IgnoreCase))
            {
                yield return parte;
                parte = "";
            }
        }
        if (parte.Length > 0) yield return parte;
    }

    /// <summary>
    /// Livelli dei capitoli dal quadro riepilogativo («Parti d'opera / Totale Ramo / Totale»): un capitolo contiene i capitoli
    /// che lo seguono quando la somma dei loro importi (più l'importo delle sue voci dirette) è uguale al suo totale.
    /// Restituisce null se il quadro non corrisponde ai capitoli letti nel computo.
    /// </summary>
    private static List<(int Livello, int Padre)>? LivelliDaRiepilogo(List<string> titoli, List<string> riepilogo)
    {
        var nodi = new List<(string Nome, decimal Totale, decimal Proprio)>();
        var nome = "";
        foreach (var r in riepilogo)
        {
            var m = Regex.Match(r, @"^(.*?)\s*€\s*(-?[\d.]+,\d{2})(?:\s*€\s*(-?[\d.]+,\d{2}))?$");
            if (!m.Success) { if (!r.StartsWith("Parti d", StringComparison.OrdinalIgnoreCase)) nome = (nome + " " + r).Trim(); continue; }
            var totale = ANumero(m.Groups[2].Value);
            nodi.Add(((nome + " " + m.Groups[1].Value).Trim(), totale, m.Groups[3].Success ? ANumero(m.Groups[3].Value) : 0));
            nome = "";
        }
        if (nodi.Count == 0) return null;
        // Il primo nodo può essere la radice («Computo»), che contiene tutti gli altri.
        var risultato = new List<(int Livello, int Padre)>();
        var indice = 0;
        var conRadice = titoli.Count == 0 || !Uguali(nodi[0].Nome, titoli[0]);
        if (conRadice) indice = 1;
        var daCoprire = conRadice ? nodi[0].Totale - nodi[0].Proprio : nodi.Sum(n => n.Totale);
        if (!Figli(nodi, ref indice, daCoprire, 1, -1, risultato) || indice != nodi.Count && !conRadice) return null;
        if (risultato.Count != titoli.Count) return null;
        for (var i = 0; i < titoli.Count; i++)
            if (!Uguali(nodi[i + (conRadice ? 1 : 0)].Nome, titoli[i])) return null;
        return risultato;
    }

    /// <summary>Assegna ai nodi seguenti il livello indicato finché i loro totali coprono l'importo; ogni nodo prova a prendersi i suoi figli.</summary>
    private static bool Figli(List<(string Nome, decimal Totale, decimal Proprio)> nodi, ref int indice, decimal daCoprire,
        int livello, int padre, List<(int Livello, int Padre)> risultato)
    {
        var somma = 0m;
        while (indice < nodi.Count && somma < daCoprire)
        {
            var (_, totale, proprio) = nodi[indice];
            var posizione = risultato.Count;
            risultato.Add((livello, padre));
            indice++;
            var (indicePrima, contoPrima) = (indice, risultato.Count);
            if (!Figli(nodi, ref indice, totale - proprio, livello + 1, posizione, risultato))
            {
                // Nessun gruppo di nodi seguenti ha per somma il suo totale: è un capitolo senza sottocapitoli.
                indice = indicePrima;
                risultato.RemoveRange(contoPrima, risultato.Count - contoPrima);
            }
            somma += totale;
        }
        return somma == daCoprire;
    }

    private static bool Uguali(string a, string b) =>
        string.Equals(Regex.Replace(a, @"\W+", ""), Regex.Replace(b, @"\W+", ""), StringComparison.OrdinalIgnoreCase);

    private sealed class Voce(int numero, string tariffa, string descrizione)
    {
        public int Numero { get; } = numero;
        public string Tariffa { get; set; } = tariffa;
        public string Descrizione { get; set; } = descrizione.Trim();
        public int Righe { get; private set; }
        public bool InMisure { get; set; }
        public List<RigaMisuraLetta> Misure { get; } = [];

        public void Aggiungi(string riga)
        {
            Righe++;
            // A capo con trattino: «sotto-» + «stante» → «sottostante».
            Descrizione = Descrizione.EndsWith('-') && riga.Length > 0 && char.IsLower(riga[0])
                ? Descrizione[..^1] + riga
                : (Descrizione + " " + riga).Trim();
        }
    }

    private sealed record RigaSommano(string? Unita, decimal Quantita, decimal Prezzo, decimal Importo);

    /// <summary>«Sommano m2 11,100 € 19,44 € 215,78» oppure «SOMMANO m² = 229,71 16,09 3.696,03».</summary>
    private static RigaSommano? Sommano(string riga)
    {
        if (!riga.StartsWith("Sommano", StringComparison.OrdinalIgnoreCase)) return null;
        var parti = riga["Sommano".Length..].Replace("€", " ").Replace("=", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numeri = parti.Reverse().TakeWhile(p => Numero().IsMatch(p)).Reverse().ToList();
        if (numeri.Count < 3) return null; // «Sommano € 64075,06»: totale di capitolo
        var unita = string.Join(' ', parti.Take(parti.Length - numeri.Count));
        return new RigaSommano(unita.Length > 0 ? unita : null, ANumero(numeri[^3]), ANumero(numeri[^2]), ANumero(numeri[^1]));
    }

    /// <summary>Riga di misura: finisce con almeno due numeri (es. «Dismissione soglia stanza 18 1,000 1,30 0,70 0,910»).</summary>
    private static bool RigaDiMisura(string riga)
    {
        var parti = riga.Split(' ');
        return parti.Length >= 2 && Numero().IsMatch(parti[^1]) && Numero().IsMatch(parti[^2]) && parti[^1].Contains(',');
    }

    private static bool IntestazioneOPagina(string riga) =>
        Regex.IsMatch(riga, @"^(Pg|Pag)\.\s*\d+$", RegexOptions.IgnoreCase)
        || Regex.IsMatch(riga, @"^(A riportare|Riporto)\b", RegexOptions.IgnoreCase)
        || riga.StartsWith("Computo metrico estimativo", StringComparison.OrdinalIgnoreCase) && riga.Length < 60
        || riga.Equals("Dimensioni Prezzo", StringComparison.OrdinalIgnoreCase)
        || IntestazioneTabella(riga);

    private static bool IntestazioneTabella(string riga) =>
        Regex.IsMatch(riga, @"^(N°|N\.|Num\.)\s.*(Descrizione|D\s?E\s?S\s?C|Designazione|Quantit)", RegexOptions.IgnoreCase);

    private static decimal ANumero(string testo) => decimal.Parse(testo.Replace(".", ""), NumberStyles.Number, It);

    private static string Abbrevia(string testo)
    {
        const int massimo = LettoreComputoExcel.LunghezzaDescrizioneBreve;
        if (testo.Length <= massimo) return testo;
        var taglio = testo.LastIndexOf(' ', massimo - 1);
        return testo[..(taglio > massimo / 2 ? taglio : massimo - 1)].TrimEnd(',', ';', ' ') + "…";
    }
}
