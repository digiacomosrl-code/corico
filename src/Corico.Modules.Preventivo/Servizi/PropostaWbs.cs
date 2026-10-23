using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Da dove viene una proposta di WBS: serve a chi conferma per capire quanto fidarsi.</summary>
public enum FontePropostaWbs
{
    /// <summary>Un'altra voce del computo con la stessa tariffa ha già questa WBS.</summary>
    StessaTariffa,
    /// <summary>Categoria o supercategoria del computo (es. la struttura di PriMus).</summary>
    Categoria,
    /// <summary>Parole in comune tra la descrizione della voce e quella della WBS.</summary>
    ParoleChiave,
    /// <summary>Servizio AI esterno (Modulo 3), sempre con conferma manuale.</summary>
    Ai,
}

/// <summary>Elemento di WBS (codice e descrizione) letto da un file o proposto; nella proposta AI, il motivo della sua posizione.</summary>
public sealed record ElementoWbsDati(string Codice, string Descrizione, string? Motivo = null);

/// <summary>Voce del computo come la vede chi propone la WBS.</summary>
public sealed record VocePerWbs(Guid Id, int Ordine, string? Tariffa, string Descrizione, string? SuperCategoria, string? Categoria,
    string? CodiceWbs, decimal RicavoLordo, string? Unita = null);

/// <summary>Dati su cui si basa una proposta: la WBS già presente nella commessa e le voci del computo attivo.</summary>
public sealed record ContestoPropostaWbs(Guid CommessaId, IReadOnlyList<ElementoWbsDati> Wbs, IReadOnlyList<VocePerWbs> Voci);

public sealed record AssegnazioneWbsProposta(Guid VoceId, string Codice, FontePropostaWbs Fonte, string Motivo);

/// <summary>
/// Proposta di WBS: elementi nuovi da aggiungere alla struttura e assegnazioni voce → codice, ognuna con fonte e motivo.
/// Non è salvata: la applica <see cref="WbsService.ApplicaPropostaAsync"/> dopo la conferma dell'utente.
/// </summary>
public sealed record PropostaWbs(IReadOnlyList<ElementoWbsDati> NuoviElementi, IReadOnlyList<AssegnazioneWbsProposta> Assegnazioni,
    IReadOnlyList<string> Note);

/// <summary>
/// Chi propone la WBS di una commessa. Restituisce solo una proposta, senza scrivere sul database.
/// Qui c'è la proposta a regole; quella con il servizio AI esterno è in <see cref="WbsAi"/>, perché richiede l'anteprima dell'invio.
/// </summary>
public interface IPropostaWbs
{
    string Nome { get; }
    Task<PropostaWbs> ProponiAsync(ContestoPropostaWbs contesto, CancellationToken ct = default);
}

/// <summary>
/// Proposta senza AI, nell'ordine: stessa tariffa già assegnata nel computo; se la commessa non ha ancora una WBS,
/// una struttura ricavata dalle supercategorie e categorie del computo; altrimenti parole in comune tra descrizioni.
/// Le voci che hanno già una WBS non vengono toccate.
/// </summary>
public sealed class PropostaWbsARegole : IPropostaWbs
{
    public string Nome => "Proposta automatica (senza AI)";

    public Task<PropostaWbs> ProponiAsync(ContestoPropostaWbs contesto, CancellationToken ct = default)
    {
        var note = new List<string>();
        var nuovi = new List<ElementoWbsDati>();
        var assegnazioni = new List<AssegnazioneWbsProposta>();
        var daAssegnare = contesto.Voci.Where(v => v.CodiceWbs is null).ToList();
        if (daAssegnare.Count == 0)
        {
            note.Add("Tutte le voci del computo hanno già una WBS: non c'è nulla da proporre.");
            return Task.FromResult(new PropostaWbs(nuovi, assegnazioni, note));
        }

        // 1. Stessa tariffa: se le voci già assegnate con quella tariffa hanno tutte la stessa WBS, la si propone.
        var perTariffa = contesto.Voci.Where(v => v.CodiceWbs is not null && v.Tariffa is not null)
            .GroupBy(v => v.Tariffa!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(v => v.CodiceWbs).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().CodiceWbs!, StringComparer.OrdinalIgnoreCase);
        foreach (var v in daAssegnare.ToList())
        {
            if (v.Tariffa is null || !perTariffa.TryGetValue(v.Tariffa.Trim(), out var codice)) continue;
            assegnazioni.Add(new(v.Id, codice, FontePropostaWbs.StessaTariffa, $"Stessa tariffa {v.Tariffa} già in WBS {codice}"));
            daAssegnare.Remove(v);
        }

        var wbs = contesto.Wbs.ToList();
        if (wbs.Count == 0)
        {
            // 2. Nessuna WBS nella commessa: struttura dalle categorie del computo.
            nuovi.AddRange(StrutturaDaCategorie(contesto.Voci, out var codici));
            if (nuovi.Count == 0)
            {
                note.Add("Il computo non ha categorie da cui ricavare la WBS. Carica la WBS aziendale da Excel o copiala da un'altra commessa, poi chiedi di nuovo la proposta.");
                return Task.FromResult(new PropostaWbs(nuovi, assegnazioni, note));
            }
            note.Add($"La commessa non aveva una WBS: ne propongo una di {nuovi.Count} elementi ricavata dalle categorie del computo. " +
                     "Se l'impresa ha una WBS aziendale, conviene caricarla prima da Excel o da un'altra commessa.");
            foreach (var v in daAssegnare)
            {
                if (codici.TryGetValue((v.SuperCategoria, v.Categoria), out var c))
                    assegnazioni.Add(new(v.Id, c, FontePropostaWbs.Categoria,
                        $"Categoria del computo «{v.Categoria ?? v.SuperCategoria}»"));
            }
        }
        else
        {
            // 3. WBS già presente: parole in comune tra la voce (con le sue categorie) e la descrizione della WBS.
            var indice = wbs.Select(e => (Elemento: e, Radici: Radici(e.Descrizione), Livello: CodiciWbs.Livello(e.Codice))).ToList();
            foreach (var v in daAssegnare)
            {
                var radiciVoce = Radici($"{v.Descrizione} {v.Categoria} {v.SuperCategoria}");
                var migliore = indice
                    .Select(x => (x.Elemento, x.Livello, Comuni: x.Radici.Keys.Where(radiciVoce.ContainsKey).Select(r => x.Radici[r]).ToList()))
                    .Where(x => x.Comuni.Count > 0)
                    .OrderByDescending(x => x.Comuni.Count).ThenByDescending(x => x.Livello).ThenBy(x => x.Elemento.Codice, CodiciWbs.Ordine)
                    .FirstOrDefault();
                if (migliore.Elemento is null) continue;
                assegnazioni.Add(new(v.Id, migliore.Elemento.Codice, FontePropostaWbs.ParoleChiave,
                    $"Parole in comune con «{migliore.Elemento.Descrizione}»: {string.Join(", ", migliore.Comuni.Select(p => $"«{p}»"))}"));
            }
        }

        var senza = contesto.Voci.Count(v => v.CodiceWbs is null) - assegnazioni.Count;
        if (senza > 0) note.Add($"{senza} voci restano senza proposta: assegnale a mano dalla tabella delle voci.");
        return Task.FromResult(new PropostaWbs(nuovi, assegnazioni, note));
    }

    /// <summary>Supercategorie → primo livello (1, 2, 3…), categorie → secondo livello (1.1, 1.2…), nell'ordine del computo.</summary>
    private static List<ElementoWbsDati> StrutturaDaCategorie(IReadOnlyList<VocePerWbs> voci,
        out Dictionary<(string?, string?), string> codici)
    {
        codici = [];
        var elementi = new List<ElementoWbsDati>();
        var primi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var figli = new Dictionary<string, int>();
        foreach (var v in voci.OrderBy(v => v.Ordine))
        {
            var sc = Pulisci(v.SuperCategoria);
            var c = Pulisci(v.Categoria);
            if (sc is null && c is null) continue;
            if (codici.ContainsKey((v.SuperCategoria, v.Categoria))) continue;
            var radice = sc ?? c!;
            if (!primi.TryGetValue(radice, out var codicePrimo))
            {
                codicePrimo = (primi.Count + 1).ToString();
                primi[radice] = codicePrimo;
                elementi.Add(new(codicePrimo, radice));
            }
            if (sc is null || c is null || string.Equals(sc, c, StringComparison.OrdinalIgnoreCase))
            {
                codici[(v.SuperCategoria, v.Categoria)] = codicePrimo;
                continue;
            }
            var n = figli.GetValueOrDefault(codicePrimo) + 1;
            figli[codicePrimo] = n;
            var codice = $"{codicePrimo}.{n}";
            elementi.Add(new(codice, c));
            codici[(v.SuperCategoria, v.Categoria)] = codice;
        }
        return elementi;
    }

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static readonly HashSet<string> ParoleVuote =
    [
        "della", "delle", "dello", "degli", "dalla", "dalle", "nella", "nelle", "sulla", "sulle", "come", "ogni", "tipo",
        "fornitura", "posa", "opera", "opere", "lavori", "compreso", "compresi", "compresa", "comprese", "incluso", "inclusi",
        "eseguito", "eseguita", "realizzazione", "secondo", "previsto", "prevista", "mediante", "altro", "quanto", "oneri",
        "onere", "magistero", "materiale", "materiali", "ditta", "direzione", "regola", "arte", "misura", "spessore",
    ];

    /// <summary>
    /// Radici delle parole significative (minuscolo, senza accenti, almeno 4 lettere, prime 6 lettere), con la parola
    /// originale per il motivo: «pavimenti» e «pavimentazione» hanno la stessa radice «pavime».
    /// </summary>
    internal static Dictionary<string, string> Radici(string? testo)
    {
        var radici = new Dictionary<string, string>();
        foreach (var p in RicercaTesto.Parole(testo))
        {
            if (p.Length < 4 || ParoleVuote.Contains(p) || p.All(char.IsDigit)) continue;
            radici.TryAdd(p.Length <= 6 ? p : p[..6], p);
        }
        return radici;
    }
}
