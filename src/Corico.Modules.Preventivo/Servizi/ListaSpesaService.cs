using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Origine di una riga della Lista della Spesa: dice quanto il dato è dettagliato.</summary>
public enum OrigineRigaSpesa
{
    /// <summary>Risorsa del livello analitico.</summary>
    Risorsa,
    /// <summary>Colonna del livello sintetico (Fornitura, Fornitura e posa, Manodopera e noli).</summary>
    Sintetico,
    /// <summary>Riga dell'offerta del fornitore.</summary>
    RigaOfferta,
    /// <summary>Voce a corpo dell'offerta.</summary>
    ACorpo,
}

public sealed record RigaSpesa(
    CategoriaRisorsa Categoria, string Descrizione, string? Unita, decimal Quantita, decimal? Prezzo, decimal Importo,
    OrigineRigaSpesa Origine, string Pacchetto, string? Fornitore, string? Tariffa, string? Voce, string? Fonte, string? Codice = null)
{
    /// <summary>Fornitore indicato quando l'offerta scelta è una stima interna o non ha il fornitore.</summary>
    public const string DaAssegnare = "Da assegnare";

    /// <summary>Descrizione e unità normalizzate: identificano la stessa risorsa in voci diverse.</summary>
    public string Chiave => Descrizione.Trim().ToLowerInvariant() + "|" + (Unita ?? "").Trim().ToLowerInvariant();
}

public sealed record RigaSpesaAggregata(CategoriaRisorsa Categoria, string? Codice, string Descrizione, string? Unita, decimal Quantita,
    decimal? Prezzo, decimal Importo, int Utilizzi, string? Fornitore = null);

public sealed record ListaSpesa(IReadOnlyList<RigaSpesa> Righe, int PacchettiSenzaOfferta, int VociNonAssegnate = 0)
{
    public decimal Totale => Righe.Sum(r => r.Importo);

    public IEnumerable<(CategoriaRisorsa Categoria, decimal Importo)> PerCategoria =>
        Enum.GetValues<CategoriaRisorsa>().Select(c => (c, Righe.Where(r => r.Categoria == c).Sum(r => r.Importo)));

    /// <summary>
    /// Righe aggregate per categoria: le risorse uguali (descrizione, unità e prezzo) di voci diverse
    /// si sommano in un'unica riga con la quantità totale da acquistare.
    /// </summary>
    public IReadOnlyList<RigaSpesaAggregata> Aggregata() =>
        Righe.GroupBy(r => (r.Categoria, r.Chiave, r.Prezzo, r.Fornitore))
            .Select(g => new RigaSpesaAggregata(g.Key.Categoria, g.First().Codice, g.First().Descrizione, g.First().Unita,
                g.Sum(r => r.Quantita), g.Key.Prezzo, g.Sum(r => r.Importo), g.Count(), g.Key.Fornitore))
            .OrderBy(r => r.Categoria).ThenBy(r => r.Codice).ThenBy(r => r.Fornitore).ThenBy(r => r.Prezzo).ToList();
}

/// <summary>
/// Lista della Spesa di commessa (sez. 13.2): tutte le risorse e i costi delle offerte scelte, per categoria e per voce.
/// Livello sintetico e analitico confluiscono negli stessi totali senza doppi conteggi: il totale coincide
/// con il costo pianificato della commessa.
/// </summary>
public sealed class ListaSpesaService(IDbContextFactory<PreventivoDbContext> fabbrica)
{
    public async Task<ListaSpesa> CalcolaAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var pacchetti = await db.Pacchetti.AsNoTracking().Include(p => p.Voci)
            .Where(p => p.CommessaId == commessaId && !p.Archiviato).OrderBy(p => p.Ordine).ToListAsync(ct);
        var offerte = await PacchettiService.CaricaOfferteAsync(db, commessaId, ct);
        var voci = await PacchettiService.VociDiCalcoloAsync(db, commessaId, ct);

        var righe = new List<RigaSpesa>();
        var senzaOfferta = 0;
        foreach (var p in pacchetti)
        {
            var scelta = offerte.FirstOrDefault(o => o.Id == p.OffertaSceltaId);
            if (scelta is null) { senzaOfferta++; continue; }
            var vociPacchetto = PacchettiService.VociDel(p, voci);
            var calcolo = CalcoloPacchetti.Calcola(scelta, vociPacchetto, 0);
            var perVoce = scelta.Voci.ToDictionary(v => v.VoceComputoId);
            var fornitore = Fornitore(p, scelta);

            RigaSpesa Riga(CategoriaRisorsa c, string descrizione, string? unita, decimal q, decimal? prezzo, decimal importo,
                OrigineRigaSpesa origine, VoceDiCalcolo? voce, string? fonte) =>
                new(c, descrizione, unita, q, prezzo, importo, origine, p.Descrizione, fornitore, voce?.Tariffa, voce?.Descrizione, fonte);

            foreach (var vc in calcolo.Voci)
            {
                if (!perVoce.TryGetValue(vc.Voce.Id, out var ov)) continue;
                var q = vc.Quantita;
                if (ov.Risorse.Count > 0)
                {
                    foreach (var r in ov.Risorse.OrderBy(r => r.Ordine))
                        righe.Add(Riga(r.Categoria, r.Descrizione, r.Unita, q * Risorse.FabbisognoEffettivo(r), r.Prezzo,
                            q * Risorse.CostoUnitario(r), OrigineRigaSpesa.Risorsa, vc.Voce, r.Fonte));
                    continue;
                }
                // Livello sintetico: ogni colonna diventa una riga nella categoria corrispondente.
                if (ov.Fornitura != 0)
                    righe.Add(Riga(CategoriaRisorsa.Materiali, vc.Voce.Descrizione, vc.Voce.Unita, q, ov.Fornitura,
                        q * ov.Fornitura, OrigineRigaSpesa.Sintetico, vc.Voce, null));
                if (ov.FornituraEPosa != 0)
                    righe.Add(Riga(CategoriaRisorsa.Subappalti, vc.Voce.Descrizione, vc.Voce.Unita, q, ov.FornituraEPosa,
                        q * ov.FornituraEPosa, OrigineRigaSpesa.Sintetico, vc.Voce, null));
                if (ov.ManodoperaENoli != 0)
                    righe.Add(Riga(CategoriaRisorsa.Manodopera, vc.Voce.Descrizione, vc.Voce.Unita, q, ov.ManodoperaENoli,
                        q * ov.ManodoperaENoli, OrigineRigaSpesa.Sintetico, vc.Voce, null));
            }

            var vociPerId = vociPacchetto.ToDictionary(v => v.Id);
            foreach (var r in scelta.Righe.OrderBy(r => r.Ordine))
            {
                var voce = r.VoceComputoId is { } id ? vociPerId.GetValueOrDefault(id) : null;
                righe.Add(Riga(CategoriaRighe(scelta.Tipo), r.Descrizione, r.Unita, r.Quantita,
                    CalcoloPacchetti.PrezzoNetto(r.PrezzoListino, r.ScontoPercentuale),
                    CalcoloPacchetti.ImportoRiga(r.Quantita, r.PrezzoListino, r.ScontoPercentuale), OrigineRigaSpesa.RigaOfferta, voce,
                    scelta.Numero is null ? null : $"Offerta {scelta.Numero}"));
            }
            foreach (var c in scelta.CostiACorpo.Where(c => !c.Incluso).OrderBy(c => c.Ordine))
                righe.Add(Riga(CategoriaCorpo(c.Descrizione), c.Descrizione, "a corpo", 1, c.Importo, c.Importo, OrigineRigaSpesa.ACorpo, null,
                    scelta.Numero is null ? null : $"Offerta {scelta.Numero}"));
        }
        var assegnate = pacchetti.SelectMany(p => p.Voci).Select(v => v.VoceComputoId).ToHashSet();
        // Stesso criterio del tabellone: voci attive del computo attivo che non stanno in nessun pacchetto.
        var computoAttivo = await db.Computi.AsNoTracking().Where(c => c.CommessaId == commessaId && c.Attivo)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var attive = await db.VociComputo.AsNoTracking().Where(v => v.ComputoId == computoAttivo && !v.Archiviata)
            .Select(v => v.Id).ToListAsync(ct);
        righe = await AssegnaCodiciAsync(db, commessaId, righe, ct);
        return new ListaSpesa(righe, senzaOfferta, attive.Count(id => !assegnate.Contains(id)));
    }

    /// <summary>
    /// Fornitore della riga: quello dell'offerta scelta; «Esecuzione diretta» se l'impresa esegue in proprio;
    /// «Da assegnare» se l'offerta scelta è una stima interna o non indica il fornitore.
    /// </summary>
    public static string Fornitore(Pacchetto p, Offerta scelta) =>
        p.Tipo == TipoAffidamento.EsecuzioneDiretta ? "Esecuzione diretta"
        : scelta.Tipo == TipoOfferta.StimaInterna || string.IsNullOrWhiteSpace(scelta.NomeFornitore) ? RigaSpesa.DaAssegnare
        : scelta.NomeFornitore.Trim();

    /// <summary>Prefisso del codice risorsa per categoria.</summary>
    public static string Prefisso(CategoriaRisorsa c) => c switch
    {
        CategoriaRisorsa.Materiali => "MAT",
        CategoriaRisorsa.Manodopera => "MAN",
        CategoriaRisorsa.Attrezzature => "ATT",
        CategoriaRisorsa.Subappalti => "SUB",
        CategoriaRisorsa.TrasportiESmaltimenti => "TRA",
        _ => "ALT",
    };

    /// <summary>
    /// Codici delle risorse: quelli già assegnati nella commessa restano; le risorse nuove ricevono il numero
    /// successivo della loro categoria, nell'ordine in cui compaiono. I codici non vengono mai riutilizzati.
    /// </summary>
    private static async Task<List<RigaSpesa>> AssegnaCodiciAsync(PreventivoDbContext db, Guid commessaId, List<RigaSpesa> righe, CancellationToken ct)
    {
        var esistenti = await db.CodiciRisorse.Where(c => c.CommessaId == commessaId).ToListAsync(ct);
        var codici = esistenti.ToDictionary(c => (c.Categoria, c.Chiave), c => c.Codice);
        var ultimo = esistenti.GroupBy(c => c.Categoria).ToDictionary(g => g.Key, g => g.Max(c => c.Numero));
        var nuovi = false;
        foreach (var r in righe)
        {
            if (codici.ContainsKey((r.Categoria, r.Chiave))) continue;
            var numero = ultimo.GetValueOrDefault(r.Categoria) + 1;
            ultimo[r.Categoria] = numero;
            var codice = $"{Prefisso(r.Categoria)}.{numero:000}";
            codici[(r.Categoria, r.Chiave)] = codice;
            db.CodiciRisorse.Add(new CodiceRisorsa { CommessaId = commessaId, Categoria = r.Categoria, Chiave = r.Chiave, Numero = numero, Codice = codice });
            nuovi = true;
        }
        if (nuovi)
        {
            db.InserimentiSoloInRiepilogo = true;   // codici tecnici: nessuna voce di storico
            await db.SaveChangesAsync(ct);
        }
        return righe.Select(r => r with { Codice = codici[(r.Categoria, r.Chiave)] }).ToList();
    }

    /// <summary>Categoria delle righe d'offerta secondo il tipo di offerta.</summary>
    public static CategoriaRisorsa CategoriaRighe(TipoOfferta tipo) => tipo switch
    {
        TipoOfferta.SolaFornitura => CategoriaRisorsa.Materiali,
        TipoOfferta.FornituraEPosa or TipoOfferta.SolaPosa => CategoriaRisorsa.Subappalti,
        TipoOfferta.Nolo => CategoriaRisorsa.Attrezzature,
        _ => CategoriaRisorsa.AltriCosti,
    };

    /// <summary>Le voci a corpo di trasporto, scarico e smaltimento vanno nella loro categoria; le altre tra gli altri costi diretti.</summary>
    public static CategoriaRisorsa CategoriaCorpo(string descrizione)
    {
        var d = descrizione.ToLowerInvariant();
        string[] trasporti = ["traspor", "scaric", "smalt", "discaric", "consegn", "spediz", "imball"];
        return trasporti.Any(d.Contains) ? CategoriaRisorsa.TrasportiESmaltimenti : CategoriaRisorsa.AltriCosti;
    }
}
