using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public enum ValiditaOfferta { NonIndicata, Valida, InScadenza, Scaduta }

/// <summary>Prezzo dello stesso fornitore in un ordine emesso su un'altra commessa.</summary>
public sealed record PrezzoStorico(string Ordine, DateOnly Data, string Commessa, string Descrizione, string? Unita, decimal Prezzo);

/// <summary>Costo di una voce secondo un'offerta, nel confronto voce × offerta.</summary>
public sealed record CellaConfronto(decimal? CostoUnitario, decimal Costo, bool Quotata, decimal? ValoreStimato, bool Migliore,
    decimal? ScartoPercentuale, IReadOnlyList<PrezzoStorico> Storico);

public sealed record VoceConfronto(VoceDiCalcolo Voce, decimal Quantita, IReadOnlyList<CellaConfronto> Celle);

/// <summary>Offerta nel confronto avanzato: costo dell'offerta più esclusioni stimate e voci non quotate valorizzate.</summary>
public sealed record OffertaConfronto(Offerta Offerta, RisultatoOfferta Risultato, bool Scelta, IReadOnlyList<StimaEsclusione> Stime,
    int VociNonQuotate, decimal ValoreNonQuotate, bool ACorpo, ValiditaOfferta Validita)
{
    public decimal TotaleStime => Stime.Where(s => s.CostoACorpoId is null).Sum(s => s.Importo);

    /// <summary>Costo a pari condizioni: offerta + esclusioni stimate (non ancora trasformate in costo) + voci non quotate.</summary>
    public decimal CostoConfrontabile => Risultato.Costo + TotaleStime + ValoreNonQuotate;

    public decimal Mdc => Risultato.RicavoNetto - CostoConfrontabile;
    public decimal? MarginePercentuale => Importi.MargineSulRicavo(Risultato.RicavoNetto, CostoConfrontabile);

    /// <summary>Esclusioni stimate che, scelta l'offerta, si possono trasformare in costo aggiuntivo del pacchetto.</summary>
    public IReadOnlyList<StimaEsclusione> StimeDaTrasformare => Stime.Where(s => s.CostoACorpoId is null && s.Importo != 0).ToList();
}

public sealed record ConfrontoPacchetto(Guid PacchettoId, IReadOnlyList<OffertaConfronto> Offerte, IReadOnlyList<VoceConfronto> Voci,
    string? SceltaDa, DateTime? SceltaIl, bool ConStimaInterna)
{
    /// <summary>Offerta con il costo confrontabile più basso (stime interne escluse).</summary>
    public OffertaConfronto? Migliore => Offerte.Where(o => o.Offerta.Tipo != TipoOfferta.StimaInterna).MinBy(o => o.CostoConfrontabile);
}

/// <summary>
/// Confronto avanzato delle offerte di un pacchetto (Modulo 2, rilascio 2B): prezzo per voce con il migliore evidenziato e lo scarto,
/// esclusioni stimate in euro, voci non quotate valorizzate con la stima interna, validità, prezzi storici dello stesso fornitore
/// in ordini emessi su altre commesse. La scelta resta all'utente e si registra con data e autore.
/// </summary>
public sealed class ConfrontoService(IDbContextFactory<PreventivoDbContext> fabbrica, CommesseService commesse, TimeProvider orologio)
{
    /// <summary>Giorni entro cui un'offerta è «in scadenza».</summary>
    public const int GiorniPreavviso = 7;

    public ValiditaOfferta Validita(DateOnly? scadenza)
    {
        var oggi = DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);
        return scadenza is not { } s ? ValiditaOfferta.NonIndicata
            : s < oggi ? ValiditaOfferta.Scaduta
            : s <= oggi.AddDays(GiorniPreavviso) ? ValiditaOfferta.InScadenza
            : ValiditaOfferta.Valida;
    }

    public async Task<ConfrontoPacchetto?> ConfrontoAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.AsNoTracking().Include(x => x.Voci).FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct);
        if (p is null) return null;
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var voci = PacchettiService.VociDel(p, await PacchettiService.VociDiCalcoloAsync(db, commessaId, ct));
        var offerte = (await PacchettiService.CaricaOfferteAsync(db, commessaId, ct)).Where(o => o.PacchettoId == p.Id)
            .OrderBy(o => o.Id == p.OffertaSceltaId ? 0 : 1).ThenBy(o => o.CreatoIl).ToList();
        var ids = offerte.Select(o => o.Id).ToList();
        var stime = (await db.StimeEsclusioni.AsNoTracking().Where(s => ids.Contains(s.OffertaId)).OrderBy(s => s.Ordine).ToListAsync(ct))
            .ToLookup(s => s.OffertaId);
        var calcoli = offerte.ToDictionary(o => o.Id, o => CalcoloPacchetti.Calcola(o, voci, preventivo.RibassoPercentuale));

        // Stima interna di riferimento per valorizzare le voci non quotate: quella scelta, se è una stima, altrimenti la prima.
        var stima = offerte.FirstOrDefault(o => o.Tipo == TipoOfferta.StimaInterna);
        var costiStima = stima is null ? [] : calcoli[stima.Id].Voci.ToDictionary(v => v.Voce.Id, v => v.Costo);

        var storico = await StoricoAsync(db, commessaId, voci, offerte, ct);

        var risultati = new List<OffertaConfronto>();
        foreach (var o in offerte)
        {
            var r = calcoli[o.Id];
            // Un'offerta a corpo (nessun costo sulle voci, importo su righe non assegnate o voci a corpo) è completa per definizione.
            var aCorpo = r.CostoVoci == 0 && r.Costo > 0;
            var nonQuotate = aCorpo || o.Tipo == TipoOfferta.StimaInterna ? [] : r.Voci.Where(v => v.Costo == 0).ToList();
            var valore = nonQuotate.Sum(v => costiStima.GetValueOrDefault(v.Voce.Id));
            risultati.Add(new OffertaConfronto(o, r, o.Id == p.OffertaSceltaId, stime[o.Id].ToList(), nonQuotate.Count, valore, aCorpo,
                Validita(o.Scadenza)));
        }

        var righe = new List<VoceConfronto>();
        foreach (var v in voci)
        {
            var celle = offerte.Select(o => calcoli[o.Id].Voci.First(x => x.Voce.Id == v.Id)).ToList();
            var quotate = celle.Where(c => c.Costo > 0 && c.CostoUnitario is not null).Select(c => c.CostoUnitario!.Value).ToList();
            decimal? migliore = quotate.Count == 0 ? null : quotate.Min();
            righe.Add(new VoceConfronto(v, celle.FirstOrDefault()?.Quantita ?? v.Quantita, offerte.Select((o, i) =>
            {
                var c = celle[i];
                var quotata = c.Costo > 0;
                var aCorpo = risultati[i].ACorpo;
                return new CellaConfronto(quotata ? c.CostoUnitario : null, c.Costo, quotata || aCorpo,
                    !quotata && !aCorpo && o.Tipo != TipoOfferta.StimaInterna && costiStima.TryGetValue(v.Id, out var s) && s > 0 ? s : null,
                    quotata && migliore is { } m && c.CostoUnitario == m && quotate.Count > 1,
                    quotata && migliore is { } mm && mm > 0 && c.CostoUnitario is { } cu ? Math.Round((cu - mm) / mm * 100m, 2) : null,
                    storico.GetValueOrDefault((v.Id, o.Id)) ?? []);
            }).ToList()));
        }
        return new ConfrontoPacchetto(p.Id, risultati, righe, p.OffertaSceltaDa, p.OffertaSceltaIl, stima is not null);
    }

    /// <summary>Ultimi tre prezzi dello stesso fornitore, per voce (stessa tariffa e unità), in ordini emessi su altre commesse.</summary>
    private async Task<Dictionary<(Guid Voce, Guid Offerta), IReadOnlyList<PrezzoStorico>>> StoricoAsync(PreventivoDbContext db, Guid commessaId,
        IReadOnlyList<VoceDiCalcolo> voci, IReadOnlyList<Offerta> offerte, CancellationToken ct)
    {
        var esito = new Dictionary<(Guid, Guid), IReadOnlyList<PrezzoStorico>>();
        var tariffe = voci.Where(v => !string.IsNullOrWhiteSpace(v.Tariffa)).Select(v => v.Tariffa!.Trim().ToLowerInvariant()).ToHashSet();
        var conFornitore = offerte.Where(o => o.Tipo != TipoOfferta.StimaInterna && (o.FornitoreId is not null || !string.IsNullOrWhiteSpace(o.NomeFornitore))).ToList();
        if (tariffe.Count == 0 || conFornitore.Count == 0) return esito;

        var ordini = await db.Ordini.AsNoTracking().Include(o => o.Righe).ThenInclude(r => r.Origini)
            .Where(o => o.CommessaId != commessaId && o.Stato == StatoOrdine.Emesso).ToListAsync(ct);
        if (ordini.Count == 0) return esito;
        var idVoci = ordini.SelectMany(o => o.Righe).SelectMany(r => r.Origini).Select(x => x.VoceComputoId).Where(x => x is not null).Distinct().ToList();
        var tariffaVoce = await db.VociComputo.AsNoTracking().Where(v => idVoci.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => (v.Tariffa ?? "").Trim().ToLowerInvariant(), ct);
        var nomiCommesse = new Dictionary<Guid, string>();
        foreach (var id in ordini.Select(o => o.CommessaId).Distinct())
            nomiCommesse[id] = (await commesse.OttieniAsync(id, ct))?.Codice ?? "";

        static string Norma(string? s) => (s ?? "").Trim().ToLowerInvariant();
        foreach (var v in voci.Where(v => !string.IsNullOrWhiteSpace(v.Tariffa)))
        {
            var t = Norma(v.Tariffa);
            foreach (var o in conFornitore)
            {
                var prezzi = ordini
                    .Where(x => (o.FornitoreId is not null && x.FornitoreId == o.FornitoreId) || Norma(x.Fornitore) == Norma(o.NomeFornitore))
                    .SelectMany(x => x.Righe.Where(r => Norma(r.Codice) == t
                            || r.Origini.Any(g => g.VoceComputoId is { } id && tariffaVoce.GetValueOrDefault(id) == t))
                        .Where(r => Norma(r.Unita) == Norma(v.Unita) || string.IsNullOrWhiteSpace(r.Unita) || string.IsNullOrWhiteSpace(v.Unita))
                        .Select(r => new PrezzoStorico(x.Numero, x.Data, nomiCommesse.GetValueOrDefault(x.CommessaId, ""), r.Descrizione, r.Unita, r.Prezzo)))
                    .OrderByDescending(x => x.Data).Take(3).ToList();
                if (prezzi.Count > 0) esito[(v.Id, o.Id)] = prezzi;
            }
        }
        return esito;
    }

    // ---- Esclusioni stimate ----

    /// <summary>Esclusioni proposte dal testo dell'offerta (una per riga o separate da «;»), da completare con l'importo.</summary>
    public static IReadOnlyList<string> EsclusioniDaTesto(string? testo) =>
        (testo ?? "").ReplaceLineEndings("\n").Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('-', '•', '*', '·').Trim()).Where(x => x.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Salva le esclusioni stimate di un'offerta. Quelle già trasformate in costo del pacchetto non cambiano.</summary>
    public async Task SalvaStimeAsync(Guid commessaId, Guid offertaId, IReadOnlyList<StimaEsclusione> dati, CancellationToken ct = default)
    {
        if (dati.Any(s => string.IsNullOrWhiteSpace(s.Descrizione)))
            throw new OperazioneNonValidaException("Ogni esclusione deve avere una descrizione.");
        if (dati.Any(s => s.Importo < 0)) throw new OperazioneNonValidaException("L'importo stimato non può essere negativo.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        if (!await db.Offerte.AnyAsync(o => o.Id == offertaId && o.CommessaId == commessaId, ct))
            throw new OperazioneNonValidaException("L'offerta non esiste più.");
        var esistenti = await db.StimeEsclusioni.Where(s => s.OffertaId == offertaId).ToListAsync(ct);
        var tenute = dati.Where(d => d.Id != Guid.Empty).Select(d => d.Id).ToHashSet();
        foreach (var e in esistenti.Where(e => e.CostoACorpoId is null && !tenute.Contains(e.Id))) db.StimeEsclusioni.Remove(e);
        var ordine = esistenti.Where(e => e.CostoACorpoId is not null).Select(e => e.Ordine).DefaultIfEmpty().Max();
        foreach (var d in dati)
        {
            var e = esistenti.FirstOrDefault(x => x.Id == d.Id);
            if (e?.CostoACorpoId is not null) continue;
            if (e is null)
            {
                e = new StimaEsclusione { OffertaId = offertaId, CommessaId = commessaId };
                db.StimeEsclusioni.Add(e);
            }
            e.Ordine = ++ordine;
            e.Descrizione = d.Descrizione.Trim();
            e.Importo = d.Importo;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Trasforma un'esclusione stimata dell'offerta scelta in un costo aggiuntivo del pacchetto (voce a corpo dell'offerta).
    /// Si conferma una stima alla volta.
    /// </summary>
    public async Task TrasformaInCostoAsync(Guid commessaId, Guid stimaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var s = await db.StimeEsclusioni.FirstOrDefaultAsync(x => x.Id == stimaId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'esclusione stimata non esiste più.");
        if (s.CostoACorpoId is not null) return;
        var o = await db.Offerte.Include(x => x.CostiACorpo).FirstAsync(x => x.Id == s.OffertaId, ct);
        if (!await db.Pacchetti.AnyAsync(p => p.Id == o.PacchettoId && p.OffertaSceltaId == o.Id, ct))
            throw new OperazioneNonValidaException("Solo le esclusioni dell'offerta scelta diventano costi del pacchetto.");
        var c = new CostoACorpo
        {
            OffertaId = o.Id, CommessaId = commessaId, Ordine = o.CostiACorpo.Select(x => x.Ordine).DefaultIfEmpty().Max() + 1,
            Descrizione = $"Esclusione a carico dell'impresa: {s.Descrizione}", Importo = s.Importo,
        };
        db.CostiACorpo.Add(c);
        s.CostoACorpoId = c.Id;
        db.NotaStorico = $"Esclusione stimata trasformata in costo del pacchetto: {s.Descrizione} ({Formato.Euro(s.Importo)})";
        await db.SaveChangesAsync(ct);
    }

    // ---- Avvisi ----

    /// <summary>Offerte scelte scadute o in scadenza nei pacchetti non ancora interamente ordinati, per commessa.</summary>
    public async Task<IReadOnlyList<(Guid CommessaId, int Scadute, int InScadenza)>> ScadenzeAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var limite = DateOnly.FromDateTime(orologio.GetLocalNow().DateTime).AddDays(GiorniPreavviso);
        var scelte = await (from p in db.Pacchetti.AsNoTracking()
                            join o in db.Offerte.AsNoTracking() on p.OffertaSceltaId equals o.Id
                            where !p.Archiviato && p.StatoCosto != StatoCosto.Definitivo && o.Scadenza != null && o.Scadenza <= limite
                                  && o.Tipo != TipoOfferta.StimaInterna
                            select new { p.CommessaId, o.Scadenza }).ToListAsync(ct);
        return scelte.GroupBy(x => x.CommessaId)
            .Select(g => (g.Key, g.Count(x => Validita(x.Scadenza) == ValiditaOfferta.Scaduta), g.Count(x => Validita(x.Scadenza) == ValiditaOfferta.InScadenza)))
            .ToList();
    }
}
