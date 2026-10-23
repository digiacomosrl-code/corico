using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Stato degli acquisti di un pacchetto: costo pianificato dalle offerte, ordinato e costo attuale.</summary>
public sealed record AvanzamentoPacchetto(
    Guid PacchettoId, decimal CostoPianificato, decimal ImportoOrdinato, decimal CostoAttuale,
    decimal PercentualeOrdinata, bool TuttoOrdinato, IReadOnlyList<string> Ordini)
{
    public bool HaOrdini => Ordini.Count > 0;

    /// <summary>Costo ancora da ordinare, ai prezzi della pianificazione.</summary>
    public decimal Residuo => CostoAttuale - ImportoOrdinato;
}

public sealed record Avanzamento(IReadOnlyList<RigaSpesa> Righe, IReadOnlyDictionary<Guid, AvanzamentoPacchetto> Pacchetti)
{
    public AvanzamentoPacchetto? Di(Guid pacchettoId) => Pacchetti.GetValueOrDefault(pacchettoId);
}

/// <summary>
/// Avanzamento degli ordini emessi rispetto alla Lista della Spesa (Modulo 2, rilascio 2A): quantità già ordinata e
/// da ordinare per riga, e per pacchetto il costo attuale = importo degli ordini emessi + costo pianificato di quanto
/// resta da ordinare (decisione 1: il costo del pacchetto è l'importo dell'ordine, non quello dell'offerta).
/// Gli ordini emessi prima del Modulo 2 (dal pacchetto, senza righe collegate) coprono l'intero pacchetto.
/// </summary>
public static class AvanzamentoOrdini
{
    internal static async Task<Avanzamento> CaricaAsync(PreventivoDbContext db, Guid commessaId, IReadOnlyList<RigaSpesa> righe, CancellationToken ct)
    {
        var ordini = await db.Ordini.AsNoTracking().Include(o => o.Righe).ThenInclude(r => r.Origini)
            .Where(o => o.CommessaId == commessaId && o.Stato == StatoOrdine.Emesso).ToListAsync(ct);
        return Calcola(righe, ordini);
    }

    /// <summary>Righe della Lista della Spesa e avanzamento degli ordini emessi, in un'unica lettura.</summary>
    internal static async Task<Avanzamento> CaricaAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var (righe, _, _) = await ListaSpesaService.RigheAsync(db, commessaId, ct);
        return await CaricaAsync(db, commessaId, righe, ct);
    }

    /// <summary>Ordine emesso prima del Modulo 2: dal pacchetto, senza collegamenti alle righe della Lista della Spesa.</summary>
    public static bool Precedente(OrdineAcquisto o) =>
        o.PacchettoId is not null && o.Righe.All(r => r.Origini.Count == 0 && r.PacchettoId is null && r.Categoria is null);

    public static Avanzamento Calcola(IReadOnlyList<RigaSpesa> righe, IReadOnlyList<OrdineAcquisto> ordini)
    {
        var perChiave = new Dictionary<string, (decimal Quantita, decimal Importo, SortedSet<string> Numeri)>();
        var pacchettoDellaChiave = new Dictionary<string, Guid>();
        var extra = new Dictionary<Guid, decimal>();
        var numeriPacchetto = new Dictionary<Guid, SortedSet<string>>();

        void Somma(string chiave, decimal quantita, decimal importo, string numero)
        {
            var (q, i, n) = perChiave.TryGetValue(chiave, out var x) ? x : (0m, 0m, new SortedSet<string>());
            n.Add(numero);
            perChiave[chiave] = (q + quantita, i + importo, n);
        }

        foreach (var o in ordini.Where(o => o.Stato == StatoOrdine.Emesso))
        {
            if (Precedente(o))
            {
                var delPacchetto = righe.Where(r => r.PacchettoId == o.PacchettoId).ToList();
                var pianificato = delPacchetto.Sum(r => r.Importo);
                foreach (var r in delPacchetto)
                    Somma(r.ChiaveOrdine, r.Quantita, pianificato == 0 ? o.Totale / delPacchetto.Count : o.Totale * r.Importo / pianificato, o.Numero);
                if (delPacchetto.Count == 0) Extra(o.PacchettoId!.Value, o.Totale, o.Numero);
                continue;
            }
            foreach (var r in o.Righe)
            {
                if (r.Origini.Count == 0)
                {
                    if ((r.PacchettoId ?? o.PacchettoId) is { } pid) Extra(pid, r.Importo, o.Numero);
                    continue;
                }
                var totale = r.Origini.Sum(x => x.Quantita);
                foreach (var x in r.Origini)
                {
                    var quota = totale == 0 ? 1m / r.Origini.Count : x.Quantita / totale;
                    Somma(x.Chiave, x.Quantita, r.Importo * quota, o.Numero);
                    pacchettoDellaChiave[x.Chiave] = x.PacchettoId;
                    Numeri(x.PacchettoId).Add(o.Numero);
                }
            }
        }

        // Righe d'ordine la cui riga della Lista della Spesa non esiste più (offerta o voci cambiate dopo l'ordine):
        // il loro importo resta nel costo del pacchetto.
        var chiaviLista = righe.Select(r => r.ChiaveOrdine).ToHashSet();
        foreach (var (chiave, pid) in pacchettoDellaChiave.Where(x => !chiaviLista.Contains(x.Key)))
            extra[pid] = extra.GetValueOrDefault(pid) + perChiave[chiave].Importo;

        // Le righe con la stessa chiave (rare: la stessa risorsa due volte nella stessa voce) si ripartiscono l'ordinato.
        var risultato = new List<RigaSpesa>(righe.Count);
        var quantitaPerChiave = righe.GroupBy(r => r.ChiaveOrdine).ToDictionary(g => g.Key, g => (Totale: g.Sum(r => r.Quantita), Righe: g.Count()));
        foreach (var r in righe)
        {
            if (!perChiave.TryGetValue(r.ChiaveOrdine, out var o)) { risultato.Add(r); continue; }
            var (totale, conteggio) = quantitaPerChiave[r.ChiaveOrdine];
            var quota = totale == 0 ? 1m / conteggio : r.Quantita / totale;
            risultato.Add(r with { QuantitaOrdinata = o.Quantita * quota, ImportoOrdinato = o.Importo * quota, Ordini = o.Numeri.ToList() });
            Numeri(r.PacchettoId).UnionWith(o.Numeri);
        }

        var pacchetti = new Dictionary<Guid, AvanzamentoPacchetto>();
        foreach (var g in risultato.GroupBy(r => r.PacchettoId).Select(g => (Id: g.Key, Righe: g.ToList()))
                     .Concat(extra.Keys.Where(k => risultato.All(r => r.PacchettoId != k)).Select(k => (Id: k, Righe: new List<RigaSpesa>()))))
        {
            var pianificato = g.Righe.Sum(r => r.Importo);
            var ordinato = g.Righe.Sum(r => r.ImportoOrdinato) + extra.GetValueOrDefault(g.Id);
            var residuo = g.Righe.Sum(Residuo);
            var coperto = g.Righe.Sum(r => r.Importo - Residuo(r));
            var significative = g.Righe.Where(r => r.Quantita != 0 || r.Importo != 0).ToList();
            var numeri = numeriPacchetto.GetValueOrDefault(g.Id)?.ToList() ?? [];
            pacchetti[g.Id] = new AvanzamentoPacchetto(g.Id, pianificato, ordinato, ordinato + residuo,
                pianificato == 0 ? (numeri.Count > 0 ? 100 : 0) : coperto / pianificato * 100m,
                numeri.Count > 0 && significative.All(r => r.Ordinata), numeri);
        }
        return new Avanzamento(risultato, pacchetti);

        void Extra(Guid pacchettoId, decimal importo, string numero)
        {
            extra[pacchettoId] = extra.GetValueOrDefault(pacchettoId) + importo;
            Numeri(pacchettoId).Add(numero);
        }

        SortedSet<string> Numeri(Guid pacchettoId)
        {
            if (!numeriPacchetto.TryGetValue(pacchettoId, out var n)) numeriPacchetto[pacchettoId] = n = [];
            return n;
        }
    }

    /// <summary>Costo pianificato della parte non ancora ordinata della riga.</summary>
    public static decimal Residuo(RigaSpesa r) =>
        r.Quantita > 0 ? r.Importo * Math.Max(0, 1m - r.QuantitaOrdinata / r.Quantita)
        : r.Ordini.Count > 0 ? 0 : r.Importo;
}
