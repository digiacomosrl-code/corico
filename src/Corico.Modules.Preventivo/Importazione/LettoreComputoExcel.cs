using System.Globalization;
using Corico.Core.Calcolo;

namespace Corico.Modules.Preventivo.Importazione;

/// <summary>
/// Lettura dei computi metrici in Excel nei due formati dei file aziendali (sez. 12):
/// <list type="bullet">
/// <item>tabellare (es. COMPUTO_ALBERGO.XLSX): una riga per voce con N°, Codice, Descrizione, Un.Mis., Prezzo, Quantità,
/// Quantità Tot. e Importo, capitoli nelle righe con codice «»Cap.1 Sc.1 Par.1»;</item>
/// <item>stampa PriMus per Excel (es. «CM xls.xlsx»): voce con Nr. Ord., Tariffa e Designazione, righe di misura
/// e riga «SOMMANO» con quantità, prezzo unitario e importo; in fondo «TOTALE euro».</item>
/// </list>
/// Il risultato ha lo stesso modello del lettore XPWE, così l'importazione segue un'unica procedura.
/// </summary>
public static class LettoreComputoExcel
{
    /// <summary>Lunghezza massima della descrizione breve; il testo completo resta nella descrizione estesa.</summary>
    public const int LunghezzaDescrizioneBreve = 250;

    public sealed record Risultato(ComputoLetto Letto, decimal? TotaleDocumento, string Formato);

    public static Risultato Leggi(Stream contenuto)
    {
        var fogli = LettoreXlsx.Leggi(contenuto);
        foreach (var foglio in fogli)
        {
            if (TrovaIntestazione(foglio, ["DESIGNAZIONE", "TARIFFA"]) is { } stampa)
                return LeggiStampaPrimus(foglio, stampa);
            if (TrovaIntestazione(foglio, ["CODICE", "DESCRIZIONE", "QUANTITÀ", "IMPORTO"]) is { } tabella)
                return LeggiTabellare(foglio, tabella);
        }
        throw new FormatException(
            "Nel file non c'è un computo riconoscibile: servono le colonne Codice, Descrizione, Quantità e Importo " +
            "oppure la stampa PriMus con Tariffa e Designazione dei lavori.");
    }

    // ---- Formato tabellare ----

    private static Risultato LeggiTabellare(FoglioXlsx f, int rigaIntestazione)
    {
        var col = Colonne(f, rigaIntestazione);
        int C(params string[] nomi) => nomi.Select(n => col.GetValueOrDefault(n)).FirstOrDefault(c => c > 0);
        var cNumero = C("N°", "N.", "NR.", "NUM.");
        var cCodice = C("CODICE", "TARIFFA", "ARTICOLO");
        var cDescrizione = C("DESCRIZIONE");
        var cUnita = C("UN.MIS.", "U.M.", "UM", "UNITÀ DI MISURA");
        var cPrezzo = C("PREZZO APPL.", "PREZZO", "PREZZO UNITARIO");
        var cQuantita = C("QUANTITÀ", "QUANTITA'", "QUANTITA");
        var cQuantitaTot = C("QUANTITÀ TOT.");
        var cImporto = C("IMPORTO");
        var cWbs = C("WBS", "COD. WBS", "CODICE WBS");
        if (cCodice == 0 || cDescrizione == 0 || cPrezzo == 0 || cQuantita == 0)
            throw new FormatException("Il computo tabellare deve avere le colonne Codice, Descrizione, Prezzo e Quantità.");

        var letto = new ComputoLetto();
        var elenco = new Dictionary<(string, decimal, string), VoceElencoLetta>();
        var quantitaPerArticolo = new Dictionary<string, (decimal Somma, decimal? Dichiarata)>();
        string? idCap = null, idSc = null, idPar = null;
        decimal totaleImporti = 0, totaleCapitoli = 0;
        var differenzeImporto = 0;
        VoceComputoLetta? ultima = null;
        var descrizioni = new Dictionary<string, string>();

        for (var r = rigaIntestazione + 1; r <= f.UltimaRiga; r++)
        {
            var codice = f.Testo(r, cCodice);
            var descrizione = f.Testo(r, cDescrizione);
            if (codice is not null && codice.StartsWith('»'))
            {
                // Capitolo: «»Cap.1», «»Cap.1 Sc.2», «»Cap.1 Sc.2 Par.1».
                var parti = codice.TrimStart('»').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var id = string.Join(' ', parti);
                var (livello, etichetta) = parti.Length switch
                {
                    1 => ("SuperCategoria", parti[0]),
                    2 => ("Categoria", parti[1]),
                    _ => ("SubCategoria", parti[^1]),
                };
                letto.Categorie.Add(new CategoriaLetta(livello, id, etichetta, descrizione ?? etichetta.Replace(".", " ")));
                if (parti.Length == 1) { (idCap, idSc, idPar) = (id, null, null); totaleCapitoli += f.Numero(r, cImporto) ?? 0; }
                else if (parti.Length == 2) (idSc, idPar) = (id, null);
                else idPar = id;
                continue;
            }

            var prezzo = f.Numero(r, cPrezzo);
            var quantita = f.Numero(r, cQuantita);
            if (codice is null || prezzo is null || quantita is null)
            {
                // Riga di sola descrizione: nel formato esportato contiene il seguito troncato della riga precedente.
                if (ultima is not null && descrizione is { Length: > 3 } && codice is null && prezzo is null)
                    descrizioni[ultima.Id] = descrizioni.GetValueOrDefault(ultima.Id, "") + " " + descrizione;
                continue;
            }

            var unitaOriginale = cUnita > 0 ? f.Testo(r, cUnita) : null;
            var testo = descrizione ?? codice;
            var chiave = (codice, prezzo.Value, testo);
            if (!elenco.TryGetValue(chiave, out var ep))
            {
                ep = NuovaVoceElenco($"E{elenco.Count + 1}", codice, testo, unitaOriginale, prezzo.Value);
                elenco[chiave] = ep;
                letto.ElencoPrezzi.Add(ep);
            }

            var ordine = letto.Voci.Count + 1;
            if (cNumero > 0 && f.Numero(r, cNumero) is { } n && n == Math.Truncate(n)) ordine = (int)n;
            ultima = new VoceComputoLetta(ordine, $"R{r}", ep.Id, quantita.Value, idCap, idSc, idPar, cWbs > 0 ? f.Testo(r, cWbs) : null, []);
            letto.Voci.Add(ultima);

            if (cImporto > 0 && f.Numero(r, cImporto) is { } importo)
            {
                totaleImporti += importo;
                if (Math.Abs(Importi.Arrotonda(quantita.Value * prezzo.Value) - importo) >= 0.01m) differenzeImporto++;
            }
            if (cQuantitaTot > 0)
            {
                var (somma, _) = quantitaPerArticolo.GetValueOrDefault(codice);
                quantitaPerArticolo[codice] = (somma + quantita.Value, f.Numero(r, cQuantitaTot));
            }
        }

        // Le righe di seguito arricchiscono solo la descrizione estesa della voce di elenco.
        foreach (var (idVoce, aggiunta) in descrizioni)
        {
            var v = letto.Voci.First(x => x.Id == idVoce);
            var i = letto.ElencoPrezzi.FindIndex(e => e.Id == v.IdElenco);
            letto.ElencoPrezzi[i] = letto.ElencoPrezzi[i] with { DesEstesa = letto.ElencoPrezzi[i].DesEstesa + aggiunta };
        }

        letto.Segnalazioni.Add(new(GravitaSegnalazione.Informazione,
            $"Computo Excel tabellare: {letto.Voci.Count} righe, {letto.ElencoPrezzi.Count} articoli distinti, {letto.Categorie.Count} capitoli."));
        if (letto.ElencoPrezzi.Count > 0 && letto.ElencoPrezzi.All(e => (e.DesEstesa ?? "").Length <= 40))
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                "Le descrizioni nel file sono abbreviate a 40 caratteri: per le descrizioni complete conviene importare il file XPWE o la stampa PriMus."));
        if (differenzeImporto > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"{differenzeImporto} righe hanno un importo diverso da quantità × prezzo arrotondato al centesimo."));
        var discordanti = quantitaPerArticolo.Where(q => q.Value.Dichiarata is { } d && Math.Abs(d - q.Value.Somma) >= 0.001m).Select(q => q.Key).ToList();
        if (discordanti.Count > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"Per {discordanti.Count} articoli la «Quantità Tot.» non coincide con la somma delle quantità: {string.Join(", ", discordanti.Take(5))}{(discordanti.Count > 5 ? " e altri" : "")}."));
        else if (cQuantitaTot > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Informazione, "Controllo superato: la «Quantità Tot.» di ogni articolo è la somma delle sue quantità."));

        var totale = Importi.Arrotonda(totaleCapitoli > 0 ? totaleCapitoli : totaleImporti);
        return new Risultato(letto, totale == 0 ? null : totale, "Excel tabellare");
    }

    // ---- Stampa PriMus per Excel ----

    private static Risultato LeggiStampaPrimus(FoglioXlsx f, int rigaIntestazione)
    {
        var col = Colonne(f, rigaIntestazione);
        var sotto = Colonne(f, rigaIntestazione + 1);
        int C(Dictionary<string, int> d, params string[] nomi) => nomi.Select(n => d.GetValueOrDefault(n)).FirstOrDefault(c => c > 0);
        var cNumero = C(col, "NR. ORD.", "N.", "NR.");
        var cTariffa = C(col, "TARIFFA");
        var cDescrizione = C(col, "DESIGNAZIONE DEI LAVORI");
        var cQuantita = C(col, "QUANTITÀ", "QUANTITA'");
        var cPartiUguali = C(sotto, "PAR.UG", "PAR.UG.");
        var cLunghezza = C(sotto, "LUNG.");
        var cLarghezza = C(sotto, "LARG.");
        var cAltezza = C(sotto, "H/PESO");
        var cUnitario = C(sotto, "UNITARIO");
        var cTotale = C(sotto, "TOTALE");
        if (cTariffa == 0 || cDescrizione == 0 || cQuantita == 0 || cUnitario == 0)
            throw new FormatException("La stampa PriMus non ha le colonne attese (Tariffa, Designazione, Quantità, Unitario).");

        var letto = new ComputoLetto();
        decimal? totaleDocumento = null;
        decimal totaleImporti = 0;
        var differenzeImporto = 0;
        var senzaSommano = new List<string>();

        (int Ordine, string Tariffa, string Descrizione, List<RigaMisuraLetta> Misure)? corrente = null;
        void Chiudi() { if (corrente is { } c) senzaSommano.Add(c.Tariffa); corrente = null; }

        for (var r = rigaIntestazione + 2; r <= f.UltimaRiga; r++)
        {
            var descrizione = f.Testo(r, cDescrizione);
            var tariffa = f.Testo(r, cTariffa);
            var numero = cNumero > 0 ? f.Numero(r, cNumero) : null;

            if (descrizione is not null && descrizione.StartsWith("TOTALE", StringComparison.OrdinalIgnoreCase))
            {
                totaleDocumento = cTotale > 0 ? f.Numero(r, cTotale) : null;
                Chiudi();
                break;
            }

            if (numero is not null && tariffa is not null)
            {
                Chiudi();
                corrente = ((int)numero.Value, tariffa, descrizione ?? tariffa, []);
                continue;
            }
            if (corrente is not { } voce || descrizione is null) continue;

            if (descrizione.StartsWith("SOMMANO", StringComparison.OrdinalIgnoreCase))
            {
                var quantita = f.Numero(r, cQuantita) ?? 0;
                var prezzo = f.Numero(r, cUnitario) ?? 0;
                var unita = descrizione["SOMMANO".Length..].Trim();
                var ep = NuovaVoceElenco($"E{letto.ElencoPrezzi.Count + 1}", voce.Tariffa, voce.Descrizione,
                    string.IsNullOrEmpty(unita) ? null : unita, prezzo);
                letto.ElencoPrezzi.Add(ep);
                letto.Voci.Add(new VoceComputoLetta(voce.Ordine, $"R{r}", ep.Id, quantita, null, null, null, null, voce.Misure));
                if (cTotale > 0 && f.Numero(r, cTotale) is { } importo)
                {
                    totaleImporti += importo;
                    if (Math.Abs(Importi.Arrotonda(quantita * prezzo) - importo) >= 0.01m) differenzeImporto++;
                }
                corrente = null;
                continue;
            }

            // Intestazione delle misure e righe di parziale non sono misure.
            if (descrizione.StartsWith("M I S U R A Z I O N I", StringComparison.OrdinalIgnoreCase)
                || descrizione.StartsWith("Parziale", StringComparison.OrdinalIgnoreCase))
                continue;

            decimal? N(int c) => c > 0 ? f.Numero(r, c) : null;
            var (pu, lu, la, al, q) = (N(cPartiUguali), N(cLunghezza), N(cLarghezza), N(cAltezza), N(cQuantita));
            // Le righe di solo testo hanno zero nelle colonne numeriche.
            var soloTesto = new[] { pu, lu, la, al, q }.All(x => (x ?? 0) == 0);
            voce.Misure.Add(new RigaMisuraLetta(descrizione, pu, lu, la, al, soloTesto ? null : q, null));
        }
        if (corrente is not null) Chiudi();

        letto.Segnalazioni.Add(new(GravitaSegnalazione.Informazione,
            $"Stampa PriMus per Excel: {letto.Voci.Count} voci con le righe di misura."));
        if (senzaSommano.Count > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"{senzaSommano.Count} voci senza riga «SOMMANO» sono state ignorate: {string.Join(", ", senzaSommano.Take(5))}."));
        if (differenzeImporto > 0)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione,
                $"{differenzeImporto} voci hanno un importo diverso da quantità × prezzo arrotondato al centesimo."));
        if (totaleDocumento is null)
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Attenzione, "Nel file non c'è la riga «TOTALE euro»: il totale va verificato a mano."));

        var totale = totaleDocumento ?? (totaleImporti == 0 ? null : totaleImporti);
        return new Risultato(letto, totale is null ? null : Importi.Arrotonda(totale.Value), "Stampa PriMus per Excel");
    }

    // ---- Funzioni comuni ----

    private static VoceElencoLetta NuovaVoceElenco(string id, string tariffa, string descrizione, string? unitaOriginale, decimal prezzo) =>
        new(id, tariffa, null, null, Abbrevia(descrizione), descrizione, unitaOriginale, UnitaMisura.Normalizza(unitaOriginale), prezzo,
            null, null, null, null, null, null, null, []);

    private static string Abbrevia(string testo)
    {
        var t = testo.ReplaceLineEndings(" ").Trim();
        if (t.Length <= LunghezzaDescrizioneBreve) return t;
        var taglio = t.LastIndexOf(' ', LunghezzaDescrizioneBreve - 1);
        return t[..(taglio > LunghezzaDescrizioneBreve / 2 ? taglio : LunghezzaDescrizioneBreve - 1)].TrimEnd(',', ';', ' ') + "…";
    }

    /// <summary>Prima riga (entro le prime 30) che contiene tutte le intestazioni indicate.</summary>
    private static int? TrovaIntestazione(FoglioXlsx f, string[] richieste)
    {
        for (var r = 1; r <= Math.Min(30, f.UltimaRiga); r++)
        {
            var testi = Enumerable.Range(1, f.UltimaColonna).Select(c => Normalizza(f.Testo(r, c))).Where(t => t is not null).ToList();
            if (richieste.All(q => testi.Any(t => t!.StartsWith(q, StringComparison.Ordinal)))) return r;
        }
        return null;
    }

    private static Dictionary<string, int> Colonne(FoglioXlsx f, int riga)
    {
        var d = new Dictionary<string, int>();
        for (var c = 1; c <= f.UltimaColonna; c++)
            if (Normalizza(f.Testo(riga, c)) is { } t) d.TryAdd(t, c);
        return d;
    }

    private static string? Normalizza(string? t) =>
        string.IsNullOrWhiteSpace(t) ? null : string.Join(' ', t.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpper(CultureInfo.GetCultureInfo("it-IT"));
}
