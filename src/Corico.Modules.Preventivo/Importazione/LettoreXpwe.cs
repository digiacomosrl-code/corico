using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Corico.Modules.Preventivo.Importazione;

// Modello in memoria del computo letto, mostrato in anteprima prima del salvataggio.

public sealed record CategoriaLetta(string Livello, string Id, string? Codice, string Descrizione);

public sealed record RigaAnalisiLetta(int Tipo, string? IdElencoRiferito, string? Descrizione, string? Unita, decimal Quantita, decimal Prezzo);

public sealed record VoceElencoLetta(
    string Id, string Tariffa, string? Articolo, string? DesRidotta, string? DesBreve, string? DesEstesa,
    string? UnitaOriginale, string? Unita, decimal Prezzo,
    decimal? IncSicurezza, decimal? IncManodopera, decimal? IncMateriali, decimal? IncAttrezzature,
    string? IdSuperCapitolo, decimal? SpeseGenerali, decimal? Utile, IReadOnlyList<RigaAnalisiLetta> Analisi);

public sealed record RigaMisuraLetta(string? Descrizione, decimal? PartiUguali, decimal? Lunghezza, decimal? Larghezza,
    decimal? AltezzaPeso, decimal? Quantita, string? VoceRiferita);

public sealed record VoceComputoLetta(
    int Ordine, string Id, string? IdElenco, decimal Quantita,
    string? IdSuperCategoria, string? IdCategoria, string? IdSubCategoria, string? CodiceWbs,
    IReadOnlyList<RigaMisuraLetta> Misure);

public enum GravitaSegnalazione { Informazione, Attenzione, Errore }

public sealed record Segnalazione(GravitaSegnalazione Gravita, string Testo);

public sealed class ComputoLetto
{
    public string? Oggetto { get; init; }
    public string? Comune { get; init; }
    public string? Provincia { get; init; }
    public string? Committente { get; init; }
    public string? Impresa { get; init; }
    public List<CategoriaLetta> Categorie { get; } = [];
    public List<VoceElencoLetta> ElencoPrezzi { get; } = [];
    public List<VoceComputoLetta> Voci { get; } = [];
    public List<Segnalazione> Segnalazioni { get; } = [];
    public int CaratteriRimossi { get; init; }

    public VoceElencoLetta? Elenco(string? id) => id is null ? null : ElencoPrezzi.FirstOrDefault(e => e.Id == id);
}

/// <summary>
/// Lettura dei computi PriMus (ACCA) in formato XPWE, verificata sul campione BIANCAVILLA.XPWE (versione 5.01).
/// </summary>
public static partial class LettoreXpwe
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ComputoLetto Leggi(Stream contenuto)
    {
        var testo = Decodifica(contenuto);

        // Pulizia preliminare: caratteri di controllo non ammessi in XML (es. 0x03 in ConfQuantita).
        var rimossi = CaratteriNonValidi().Matches(testo).Count;
        testo = CaratteriNonValidi().Replace(testo, "");

        XDocument xml;
        try
        {
            xml = XDocument.Parse(testo);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new FormatException($"Il file non è un computo XPWE leggibile (riga {ex.LineNumber}): {ex.Message}", ex);
        }

        var radice = xml.Root;
        if (radice is null || radice.Name.LocalName != "PweDocumento")
            throw new FormatException("Il file non è un documento PriMus XPWE (manca l'elemento PweDocumento).");

        var dg = radice.Descendants("PweDGDatiGenerali").FirstOrDefault();
        var letto = new ComputoLetto
        {
            Oggetto = Testo(dg, "Oggetto"),
            Comune = Testo(dg, "Comune"),
            Provincia = Testo(dg, "Provincia"),
            Committente = Testo(dg, "Committente"),
            Impresa = Testo(dg, "Impresa"),
            CaratteriRimossi = rimossi,
        };

        LeggiCategorie(radice, letto);
        LeggiElencoPrezzi(radice, letto);
        LeggiVoci(radice, letto);
        Verifica(letto);
        return letto;
    }

    private static void LeggiCategorie(XElement radice, ComputoLetto letto)
    {
        foreach (var (elemento, livello) in new[]
                 {
                     ("DGSuperCategorieItem", "SuperCategoria"), ("DGCategorieItem", "Categoria"), ("DGSubCategorieItem", "SubCategoria"),
                     ("DGSuperCapitoliItem", "SuperCapitolo"), ("DGCapitoliItem", "Capitolo"), ("DGSubCapitoliItem", "SubCapitolo"),
                 })
        {
            foreach (var e in radice.Descendants(elemento))
            {
                var id = (string?)e.Attribute("ID");
                if (id is null) continue;
                letto.Categorie.Add(new CategoriaLetta(livello, id, Testo(e, "Codice"),
                    Testo(e, "DesSintetica") ?? Testo(e, "DesEstesa") ?? $"{livello} {id}"));
            }
        }
    }

    private static void LeggiElencoPrezzi(XElement radice, ComputoLetto letto)
    {
        foreach (var e in radice.Descendants("EPItem"))
        {
            var id = (string?)e.Attribute("ID");
            if (id is null) continue;
            var um = Testo(e, "UnMisura");
            var analisi = e.Element("PweEPAnalisi");
            var righe = analisi?.Descendants("EPARItem").Select(r => new RigaAnalisiLetta(
                (int)(Numero(r, "Tipo") ?? 0),
                Numero(r, "IDEP") is > 0 ? Testo(r, "IDEP") : null,
                Testo(r, "Descrizione"), Testo(r, "Misura"),
                Numero(r, "Qt") ?? 0, Numero(r, "Prezzo") ?? 0)).ToList() ?? [];

            letto.ElencoPrezzi.Add(new VoceElencoLetta(
                id, Testo(e, "Tariffa") ?? "", Testo(e, "Articolo"),
                Testo(e, "DesRidotta"), Testo(e, "DesBreve"), Testo(e, "DesEstesa"),
                um, UnitaMisura.Normalizza(um), Numero(e, "Prezzo1") ?? 0,
                Numero(e, "IncSIC"), Numero(e, "IncMDO"), Numero(e, "IncMAT"), Numero(e, "IncATTR"),
                Numero(e, "IDSpCap") is > 0 ? Testo(e, "IDSpCap") : null,
                righe.Count > 0 ? Numero(analisi, "Spese") : null,
                righe.Count > 0 ? Numero(analisi, "Utili") : null,
                righe));
        }
    }

    private static void LeggiVoci(XElement radice, ComputoLetto letto)
    {
        var ordine = 0;
        foreach (var v in radice.Descendants("VCItem"))
        {
            var misure = v.Descendants("RGItem").Select(r => new RigaMisuraLetta(
                Testo(r, "Descrizione"), Numero(r, "PartiUguali"), Numero(r, "Lunghezza"), Numero(r, "Larghezza"),
                Numero(r, "HPeso"), Numero(r, "Quantita"),
                Numero(r, "IDVV") is > 0 ? Testo(r, "IDVV") : null)).ToList();

            // La quantità della voce è quella calcolata da PriMus (comprende "vedi voce" e sottrazioni).
            letto.Voci.Add(new VoceComputoLetta(
                ++ordine, (string?)v.Attribute("ID") ?? ordine.ToString(Inv),
                Numero(v, "IDEP") is > 0 ? Testo(v, "IDEP") : null,
                Numero(v, "Quantita") ?? 0,
                Id(v, "IDSpCat"), Id(v, "IDCat"), Id(v, "IDSbCat"), Testo(v, "CodiceWbs"), misure));
        }
    }

    private static void Verifica(ComputoLetto letto)
    {
        var s = letto.Segnalazioni;
        if (letto.CaratteriRimossi > 0)
            s.Add(new(GravitaSegnalazione.Informazione,
                $"Rimossi {letto.CaratteriRimossi} caratteri di controllo non validi; i dati non sono stati alterati."));

        var senzaElenco = letto.Voci.Where(v => letto.Elenco(v.IdElenco) is null).ToList();
        if (senzaElenco.Count > 0)
            s.Add(new(GravitaSegnalazione.Attenzione,
                $"{senzaElenco.Count} voci di computo non sono collegate a una voce di elenco prezzi valida " +
                $"(n. {string.Join(", ", senzaElenco.Take(30).Select(v => v.Ordine))}{(senzaElenco.Count > 30 ? "…" : "")}): " +
                "verranno importate con prezzo zero e restano da completare."));

        var normalizzate = letto.ElencoPrezzi
            .Where(e => e.UnitaOriginale is not null && e.Unita != e.UnitaOriginale)
            .GroupBy(e => (e.UnitaOriginale, e.Unita))
            .Select(g => $"«{g.Key.UnitaOriginale}» → «{g.Key.Unita}» ({g.Count()})").ToList();
        if (normalizzate.Count > 0)
            s.Add(new(GravitaSegnalazione.Informazione, "Unità di misura uniformate: " + string.Join(", ", normalizzate) + "."));

        var senzaUnita = letto.ElencoPrezzi.Count(e => e.Unita is null);
        if (senzaUnita > 0)
            s.Add(new(GravitaSegnalazione.Attenzione, $"{senzaUnita} voci di elenco prezzi senza unità di misura."));

        var tariffeDoppie = letto.ElencoPrezzi.Where(e => e.Tariffa.Length > 0).GroupBy(e => e.Tariffa).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (tariffeDoppie.Count > 0)
            s.Add(new(GravitaSegnalazione.Attenzione, $"Codici tariffa duplicati nell'elenco prezzi: {string.Join(", ", tariffeDoppie.Take(20))}."));

        var zero = letto.Voci.Count(v => v.Quantita == 0);
        if (zero > 0)
            s.Add(new(GravitaSegnalazione.Attenzione, $"{zero} voci con quantità zero."));

        var sicurezza = letto.Voci.Count(v => letto.Elenco(v.IdElenco)?.IncSicurezza > 0);
        if (sicurezza > 0)
            s.Add(new(GravitaSegnalazione.Informazione,
                $"{sicurezza} voci hanno un'incidenza di sicurezza: la quota corrispondente è proposta come non soggetta a ribasso."));
    }

    private static readonly Encoding Utf8Rigoroso = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>
    /// I file PriMus possono essere in UTF-8 (con o senza BOM) oppure in Windows-1252 senza dichiarazione:
    /// si prova l'UTF-8 rigoroso e, se i byte non sono validi, si legge come Windows-1252.
    /// </summary>
    internal static string Decodifica(Stream contenuto)
    {
        using var ms = new MemoryStream();
        contenuto.CopyTo(ms);
        var byteFile = ms.ToArray();
        if (byteFile.Length >= 3 && byteFile[0] == 0xEF && byteFile[1] == 0xBB && byteFile[2] == 0xBF)
            return Encoding.UTF8.GetString(byteFile, 3, byteFile.Length - 3);
        if (byteFile.Length >= 2 && byteFile[0] == 0xFF && byteFile[1] == 0xFE)
            return Encoding.Unicode.GetString(byteFile, 2, byteFile.Length - 2);
        try
        {
            return Utf8Rigoroso.GetString(byteFile);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(byteFile);
        }
    }

    private static string? Id(XElement e, string nome) => Numero(e, nome) is > 0 ? Testo(e, nome) : null;

    private static string? Testo(XElement? e, string nome)
    {
        var v = e?.Element(nome)?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static decimal? Numero(XElement? e, string nome)
    {
        var v = Testo(e, nome);
        return v is not null && decimal.TryParse(v, NumberStyles.Float, Inv, out var d) ? d : null;
    }

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex CaratteriNonValidi();
}
