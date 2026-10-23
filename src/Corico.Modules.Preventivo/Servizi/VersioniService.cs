using System.Text.Json;
using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Interfaccia;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record DifferenzaTotale(string Grandezza, decimal? Prima, decimal? Dopo, bool Percentuale = false)
{
    public decimal? Differenza => Prima is null || Dopo is null ? null : Dopo - Prima;
    public bool Cambiata => Prima is null != Dopo is null || Importi.Arrotonda(Differenza ?? 0) != 0;
}

public enum EsitoConfronto { Aggiunto, Eliminato, Modificato }

public sealed record DifferenzaPacchetto(string Descrizione, EsitoConfronto Esito,
    string? FornitorePrima, string? FornitoreDopo, StatoCosto? StatoPrima, StatoCosto? StatoDopo,
    decimal? RicavoPrima, decimal? RicavoDopo, decimal? CostoPrima, decimal? CostoDopo)
{
    public decimal? MdcPrima => RicavoPrima is null || CostoPrima is null ? null : RicavoPrima - CostoPrima;
    public decimal? MdcDopo => RicavoDopo is null || CostoDopo is null ? null : RicavoDopo - CostoDopo;
}

public sealed record DifferenzaVoce(string? Tariffa, string Descrizione, EsitoConfronto Esito,
    decimal? QuantitaPrima, decimal? QuantitaDopo, decimal? PrezzoPrima, decimal? PrezzoDopo,
    decimal? NettoPrima, decimal? NettoDopo);

public sealed record Confronto(string EtichettaPrima, string EtichettaDopo,
    IReadOnlyList<DifferenzaTotale> Totali, IReadOnlyList<DifferenzaPacchetto> Pacchetti, IReadOnlyList<DifferenzaVoce> Voci)
{
    public bool NessunaDifferenza => Totali.All(t => !t.Cambiata) && Pacchetti.Count == 0 && Voci.Count == 0;
}

/// <summary>
/// Versioni ufficiali del preventivo: pubblicazione con nota obbligatoria di una fotografia
/// immutabile della bozza e confronto tra due versioni (o tra una versione e la bozza).
/// </summary>
public sealed class VersioniService(IDbContextFactory<PreventivoDbContext> fabbrica, RiepilogoService riepilogo)
{
    internal static readonly JsonSerializerOptions OpzioniJson = new() { WriteIndented = false };

    public const string EtichettaBozza = "Bozza attuale";

    public async Task<IReadOnlyList<VersionePreventivo>> ElencoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Versioni.AsNoTracking().Where(v => v.CommessaId == commessaId)
            .OrderByDescending(v => v.Numero).ToListAsync(ct);
    }

    public async Task<VersionePreventivo> PubblicaAsync(Guid commessaId, string nota, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nota))
            throw new OperazioneNonValidaException("La nota è obbligatoria: indica il motivo della nuova versione.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var f = await riepilogo.FotografiaAsync(db, commessaId, ct);
        var numero = (await db.Versioni.Where(v => v.CommessaId == commessaId).MaxAsync(v => (int?)v.Numero, ct) ?? 0) + 1;
        var v = new VersionePreventivo
        {
            CommessaId = commessaId,
            Numero = numero,
            Nota = nota.Trim(),
            Fotografia = JsonSerializer.Serialize(f, OpzioniJson),
            RibassoPercentuale = f.RibassoPercentuale,
            Ricavo = f.Totali.Ricavo,
            Costo = f.Totali.Costo,
            Mdc = f.Totali.Mdc,
            SpeseGenerali = f.Totali.SpeseGenerali,
            Risultato = f.Totali.Risultato,
        };
        db.Versioni.Add(v);
        // La fotografia completa resta nella versione: nello storico basta il riepilogo.
        db.InserimentiSoloInRiepilogo = true;
        db.RegistraOperazione("Pubblicazione", nameof(VersionePreventivo), v.Id, commessaId,
            $"Versione ufficiale n. {numero}: {v.Nota}",
            dopo: new { v.Numero, Ricavo = Importi.Arrotonda(v.Ricavo), Costo = Importi.Arrotonda(v.Costo), Risultato = Importi.Arrotonda(v.Risultato) });
        await db.SaveChangesAsync(ct);
        return v;
    }

    public async Task<Fotografia?> FotografiaAsync(Guid commessaId, Guid versioneId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var v = await db.Versioni.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versioneId && x.CommessaId == commessaId, ct);
        return v is null ? null : Leggi(v);
    }

    /// <summary>Confronta due versioni; un identificativo null indica la bozza attuale.</summary>
    public async Task<Confronto> ConfrontaAsync(Guid commessaId, Guid? primaId, Guid? dopoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        async Task<(Fotografia, string)> Carica(Guid? id)
        {
            if (id is null) return (await riepilogo.FotografiaAsync(db, commessaId, ct), EtichettaBozza);
            var v = await db.Versioni.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
                ?? throw new OperazioneNonValidaException("La versione indicata non esiste.");
            return (Leggi(v), Etichetta(v));
        }
        var (prima, etichettaPrima) = await Carica(primaId);
        var (dopo, etichettaDopo) = await Carica(dopoId);
        return Confronta(prima, dopo, etichettaPrima, etichettaDopo);
    }

    /// <summary>Impatto della pubblicazione: differenze tra l'ultima versione ufficiale e la bozza; null se non ci sono versioni.</summary>
    public async Task<Confronto?> ImpattoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var ultima = await db.Versioni.AsNoTracking().Where(v => v.CommessaId == commessaId)
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
        if (ultima is null) return null;
        return Confronta(Leggi(ultima), await riepilogo.FotografiaAsync(db, commessaId, ct), Etichetta(ultima), EtichettaBozza);
    }

    /// <summary>Quantità che genera il ricavo: quella di pianificazione dell'offerta scelta, se diversa dal computo.</summary>
    private static decimal Quantita(VoceFotografia v) => v.QuantitaPianificazione ?? v.Quantita;

    private static (string, string) Chiave(VoceFotografia v) => ((v.Tariffa ?? "").Trim().ToUpperInvariant(), v.Descrizione.Trim());

    public static string Etichetta(VersionePreventivo v) => v.EBudget
        ? $"Budget {v.NumeroBudget} ({Testi.StatoBudget(v.StatoBudget!.Value).ToLowerInvariant()}) del {Formato.DataOra(v.CreatoIl)}"
        : $"Versione storica n. {v.Numero} del {Formato.DataOra(v.CreatoIl)}";

    internal static Fotografia Leggi(VersionePreventivo v) =>
        JsonSerializer.Deserialize<Fotografia>(v.Fotografia, OpzioniJson)
        ?? throw new InvalidOperationException($"Fotografia della versione {v.Numero} non leggibile.");

    public static Confronto Confronta(Fotografia a, Fotografia b, string etichettaA, string etichettaB)
    {
        var ta = a.Totali;
        var tb = b.Totali;
        var totali = new List<DifferenzaTotale>
        {
            new("Ribasso d'asta", a.RibassoPercentuale, b.RibassoPercentuale, true),
            new("Importo lavori (lordo)", ta.ImportoLavoriLordo, tb.ImportoLavoriLordo),
            new("Ricavo netto da computo", ta.RicavoNettoComputo, tb.RicavoNettoComputo),
            new("Ricavo di commessa", ta.Ricavo, tb.Ricavo),
            new("Costo pianificato", ta.Costo, tb.Costo),
            new("MDC", ta.Mdc, tb.Mdc),
            new("Margine sui ricavi", ta.MarginePercentuale, tb.MarginePercentuale, true),
            new("Spese generali", ta.SpeseGenerali, tb.SpeseGenerali),
            new("Risultato", ta.Risultato, tb.Risultato),
            new("Ricavo senza costo pianificato", ta.RicavoSenzaCosto, tb.RicavoSenzaCosto),
        };

        var pacchetti = new List<DifferenzaPacchetto>();
        var pa = a.Pacchetti.ToDictionary(p => p.Id);
        var pb = b.Pacchetti.ToDictionary(p => p.Id);
        foreach (var p in b.Pacchetti)
        {
            if (!pa.TryGetValue(p.Id, out var x))
                pacchetti.Add(new(p.Descrizione, EsitoConfronto.Aggiunto, null, p.Fornitore, null, p.Stato, null, p.Ricavo, null, p.Costo));
            else if (x.Fornitore != p.Fornitore || x.Stato != p.Stato || Importi.Arrotonda(x.Ricavo) != Importi.Arrotonda(p.Ricavo)
                     || (x.Costo is null ? null : (decimal?)Importi.Arrotonda(x.Costo.Value)) != (p.Costo is null ? null : Importi.Arrotonda(p.Costo.Value)))
                pacchetti.Add(new(p.Descrizione, EsitoConfronto.Modificato, x.Fornitore, p.Fornitore, x.Stato, p.Stato, x.Ricavo, p.Ricavo, x.Costo, p.Costo));
        }
        foreach (var x in a.Pacchetti.Where(x => !pb.ContainsKey(x.Id)))
            pacchetti.Add(new(x.Descrizione, EsitoConfronto.Eliminato, x.Fornitore, null, x.Stato, null, x.Ricavo, null, x.Costo, null));

        // Abbinamento delle voci per identificativo; quelle rimaste (es. computo reimportato dopo una perizia)
        // si abbinano per tariffa e descrizione, nell'ordine del computo.
        var coppie = new List<(VoceFotografia? Prima, VoceFotografia? Dopo)>();
        var va = a.Voci.ToDictionary(v => v.Id);
        var restantiB = new List<VoceFotografia>();
        foreach (var v in b.Voci)
        {
            if (va.Remove(v.Id, out var x)) coppie.Add((x, v));
            else restantiB.Add(v);
        }
        var perChiave = va.Values.OrderBy(v => v.Ordine).GroupBy(Chiave).ToDictionary(g => g.Key, g => new Queue<VoceFotografia>(g));
        foreach (var v in restantiB)
            coppie.Add((perChiave.TryGetValue(Chiave(v), out var coda) && coda.Count > 0 ? coda.Dequeue() : null, v));
        coppie.AddRange(perChiave.Values.SelectMany(q => q).Select(x => ((VoceFotografia?)x, (VoceFotografia?)null)));

        var voci = new List<DifferenzaVoce>();
        foreach (var (x, v) in coppie)
        {
            if (x is null)
                voci.Add(new(v!.Tariffa, v.Descrizione, EsitoConfronto.Aggiunto, null, Quantita(v), null, v.Prezzo, null, v.RicavoNetto));
            else if (v is null)
                voci.Add(new(x.Tariffa, x.Descrizione, EsitoConfronto.Eliminato, Quantita(x), null, x.Prezzo, null, x.RicavoNetto, null));
            else if (Quantita(x) != Quantita(v) || x.Prezzo != v.Prezzo || x.PercentualeNonSoggetta != v.PercentualeNonSoggetta
                     || Importi.Arrotonda(x.RicavoNetto) != Importi.Arrotonda(v.RicavoNetto))
                voci.Add(new(v.Tariffa, v.Descrizione, EsitoConfronto.Modificato, Quantita(x), Quantita(v), x.Prezzo, v.Prezzo, x.RicavoNetto, v.RicavoNetto));
        }

        return new Confronto(etichettaA, etichettaB, totali, pacchetti, voci);
    }
}
