using System.Globalization;
using System.Text.Json;
using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Riga della pagina «Aggiorna prezzi»: prezzo di costo attuale e nuovo, con l'effetto su costo e MDC.</summary>
public sealed record PropostaPrezzo(
    string Id, FontePrezzo Fonte, Guid PacchettoId, string Pacchetto, string? Tariffa, string? Voce, string Descrizione, string? Unita,
    decimal Quantita, decimal PrezzoAttuale, decimal PrezzoNuovo, decimal QuotaDaOrdinare, string? Nota = null)
{
    /// <summary>Effetto sul costo attuale: vale solo per la parte ancora da ordinare (quella ordinata è al prezzo dell'ordine).</summary>
    public decimal DifferenzaCosto => Importi.Arrotonda((PrezzoNuovo - PrezzoAttuale) * Quantita * QuotaDaOrdinare);

    public decimal DifferenzaMdc => -DifferenzaCosto;

    /// <summary>Gli ordini emessi si applicano da soli all'emissione: qui compaiono solo per memoria.</summary>
    public bool SoloMemoria => Fonte == FontePrezzo.OrdineEmesso;

    internal ElementoPrezzo Elemento { get; init; }
    internal Guid ElementoId { get; init; }
    internal string? Campo { get; init; }
    internal Guid? AnalisiId { get; init; }
    internal Guid? NuovaOffertaId { get; init; }
}

public sealed record PropostePrezzi(IReadOnlyList<PropostaPrezzo> Righe, decimal Ricavo, decimal CostoAttuale)
{
    public decimal Mdc => Ricavo - CostoAttuale;
    public decimal? MarginePercentuale => Importi.MargineSulRicavo(Ricavo, CostoAttuale);

    public (decimal Costo, decimal Mdc, decimal? Margine) Dopo(IEnumerable<PropostaPrezzo> scelte)
    {
        var diff = scelte.Where(s => !s.SoloMemoria).Sum(s => s.DifferenzaCosto);
        return (CostoAttuale + diff, Mdc - diff, Importi.MargineSulRicavo(Ricavo, CostoAttuale + diff));
    }
}

/// <summary>
/// Aggiornamento selettivo dei prezzi di costo della bozza (Modulo 2, rilascio 2B): raccoglie i prezzi superati (analisi aziendali
/// modificate, nuove revisioni delle offerte scelte, variazioni percentuali per categoria; gli ordini emessi per memoria) e applica
/// solo le righe scelte, mostrando prima l'effetto su costo e MDC. Non tocca i prezzi contrattuali né le versioni ufficiali.
/// La conferma è un'operazione rilevante, annullabile in blocco finché le stesse righe non sono state modificate di nuovo.
/// </summary>
public sealed class AggiornamentoPrezziService(
    IDbContextFactory<PreventivoDbContext> fabbrica, PacchettiService pacchetti, IUtenteCorrente utente, TimeProvider orologio)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private sealed record StatoRisorsa(int Ordine, CategoriaRisorsa Categoria, string Descrizione, string? Unita, decimal Fabbisogno,
        decimal SfridoPercentuale, decimal Prezzo, string? Fonte, string? Note, Guid? AnalisiId);

    public async Task<PropostePrezzi> ProposteAsync(Guid commessaId, IReadOnlyDictionary<CategoriaRisorsa, decimal>? variazioni = null,
        CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await ProposteAsync(db, commessaId, variazioni, ct);
    }

    private async Task<PropostePrezzi> ProposteAsync(PreventivoDbContext db, Guid commessaId, IReadOnlyDictionary<CategoriaRisorsa, decimal>? variazioni,
        CancellationToken ct)
    {
        var tabellone = await pacchetti.TabelloneAsync(commessaId, ct);
        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        var righe = new List<PropostaPrezzo>();

        // 1. Ordini emessi a prezzo diverso dal pianificato (solo per memoria).
        var nomi = await db.Pacchetti.AsNoTracking().Where(p => p.CommessaId == commessaId).ToDictionaryAsync(p => p.Id, p => p.Descrizione, ct);
        var ordini = await db.Ordini.AsNoTracking().Include(o => o.Righe)
            .Where(o => o.CommessaId == commessaId && o.Stato == StatoOrdine.Emesso).ToListAsync(ct);
        foreach (var o in ordini.OrderBy(o => o.Anno).ThenBy(o => o.Progressivo))
        foreach (var r in o.Righe.Where(r => r.PrezzoPianificato is { } pp && pp != r.Prezzo).OrderBy(r => r.Ordine))
        {
            var pid = r.PacchettoId ?? o.PacchettoId ?? Guid.Empty;
            righe.Add(new PropostaPrezzo($"ODA:{r.Id:N}", FontePrezzo.OrdineEmesso, pid, nomi.GetValueOrDefault(pid, "—"), r.Codice, null,
                r.Descrizione, r.Unita, r.Quantita, r.PrezzoPianificato!.Value, r.Prezzo, 1, $"{o.Numero}{(r.NotaScostamento is { } n ? ": " + n : "")}"));
        }

        // 2. Nuove revisioni delle offerte scelte.
        var avanzamento = lista.Pacchetti;
        var offerte = await PacchettiService.CaricaOfferteAsync(db, commessaId, ct);
        var voci = await PacchettiService.VociDiCalcoloAsync(db, commessaId, ct);
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var elencoPacchetti = await db.Pacchetti.AsNoTracking().Include(p => p.Voci)
            .Where(p => p.CommessaId == commessaId && !p.Archiviato && p.OffertaSceltaId != null).OrderBy(p => p.Ordine).ToListAsync(ct);
        var conRevisione = new HashSet<Guid>();
        foreach (var p in elencoPacchetti)
        {
            var s = offerte.FirstOrDefault(o => o.Id == p.OffertaSceltaId);
            if (s is null || s.Tipo == TipoOfferta.StimaInterna) continue;
            var nuova = offerte.Where(o => o.PacchettoId == p.Id && o.Id != s.Id && o.CreatoIl > s.CreatoIl && StessoFornitore(o, s)
                    && (s.Numero is null || string.Equals(o.Numero?.Trim(), s.Numero.Trim(), StringComparison.OrdinalIgnoreCase))
                    && o.Revisione is not null && !string.Equals(o.Revisione.Trim(), s.Revisione?.Trim(), StringComparison.OrdinalIgnoreCase))
                .MaxBy(o => o.CreatoIl);
            if (nuova is null) continue;
            var vociP = PacchettiService.VociDel(p, voci);
            var prima = CalcoloPacchetti.Calcola(s, vociP, preventivo.RibassoPercentuale).Costo;
            var dopo = CalcoloPacchetti.Calcola(nuova, vociP, preventivo.RibassoPercentuale).Costo;
            var a = avanzamento.GetValueOrDefault(p.Id);
            var quota = a is { CostoPianificato: > 0 } ? Math.Clamp(a.Residuo / a.CostoPianificato, 0, 1) : 1;
            conRevisione.Add(p.Id);
            righe.Add(new PropostaPrezzo($"REV:{p.Id:N}:{nuova.Id:N}", FontePrezzo.Revisione, p.Id, p.Descrizione, null, null,
                $"{s.NomeFornitore}: offerta {nuova.Numero} rev. {nuova.Revisione} al posto della rev. {s.Revisione ?? "iniziale"}", "a corpo",
                1, prima, dopo, quota, a is { HaOrdini: true } ? "Il pacchetto ha ordini emessi: cambia solo il costo della parte da ordinare" : null)
            {
                Elemento = ElementoPrezzo.Scelta, ElementoId = p.Id, NuovaOffertaId = nuova.Id,
            });
        }

        // 3. Analisi aziendali modificate dopo l'applicazione alla voce.
        var daAnalisi = lista.Righe.Where(r => r.Elemento == ElementoPrezzo.Risorsa && r.AnalisiId is not null).ToList();
        var idRisorse = daAnalisi.Select(r => r.ElementoId!.Value).ToList();
        var risorse = await db.RisorseVoci.AsNoTracking().Where(r => idRisorse.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var idAnalisi = daAnalisi.Select(r => r.AnalisiId!.Value).Distinct().ToList();
        var analisi = await db.AnalisiAziendali.AsNoTracking().Include(a => a.Righe)
            .Where(a => idAnalisi.Contains(a.Id) && a.Stato == StatoAnalisi.Confermata).ToDictionaryAsync(a => a.Id, ct);
        var offerteVoci = offerte.SelectMany(o => o.Voci).ToDictionary(v => v.Id);
        var conAnalisi = new HashSet<Guid>();
        foreach (var g in daAnalisi.Where(r => risorse.ContainsKey(r.ElementoId!.Value))
                     .GroupBy(r => (OffertaVoce: risorse[r.ElementoId!.Value].OffertaVoceId, Analisi: r.AnalisiId!.Value)))
        {
            if (!analisi.TryGetValue(g.Key.Analisi, out var an) || !offerteVoci.TryGetValue(g.Key.OffertaVoce, out var ov)) continue;
            var attuali = g.Select(r => risorse[r.ElementoId!.Value]).OrderBy(r => r.Ordine).ToList();
            var nuove = an.Righe.OrderBy(r => r.Ordine).ToList();
            if (attuali.Select(Firma).SequenceEqual(nuove.Select(Firma))) continue;
            var primo = g.First();
            var q = ov.QuantitaPianificazione ?? voci.GetValueOrDefault(ov.VoceComputoId).Voce?.Quantita ?? 0;
            var importo = g.Sum(r => r.Importo);
            var quota = importo > 0 ? Math.Clamp(g.Sum(AvanzamentoOrdini.Residuo) / importo, 0, 1) : 1;
            foreach (var r in attuali) conAnalisi.Add(r.Id);
            righe.Add(new PropostaPrezzo($"ANA:{ov.Id:N}:{an.Id:N}", FontePrezzo.Analisi, primo.PacchettoId, primo.Pacchetto, primo.Tariffa, primo.Voce,
                $"Analisi {an.Codice ?? an.Descrizione}: risorse aggiornate ({nuove.Count})", voci.GetValueOrDefault(ov.VoceComputoId).Voce?.Unita,
                q, attuali.Sum(Risorse.CostoUnitario), nuove.Sum(Risorse.CostoUnitario), quota)
            {
                Elemento = ElementoPrezzo.RisorseAnalisi, ElementoId = ov.Id, AnalisiId = an.Id,
            });
        }

        // 4. Variazioni percentuali per categoria, sulle parti ancora da ordinare.
        if (variazioni is { Count: > 0 })
        {
            var idRighe = lista.Righe.Where(r => r.Elemento == ElementoPrezzo.RigaOfferta).Select(r => r.ElementoId!.Value).ToList();
            var righeOfferta = offerte.SelectMany(o => o.Righe).Where(r => idRighe.Contains(r.Id)).ToDictionary(r => r.Id);
            foreach (var r in lista.Righe)
            {
                if (r.Elemento is not { } el || r.ElementoId is not { } id || r.Prezzo is not { } prezzo) continue;
                if (!variazioni.TryGetValue(r.Categoria, out var perc) || perc == 0) continue;
                if (el == ElementoPrezzo.Risorsa && conAnalisi.Contains(id)) continue;
                var quota = r.Quantita > 0 ? Math.Max(0, 1m - r.QuantitaOrdinata / r.Quantita) : r.Ordini.Count > 0 ? 0 : 1;
                if (quota == 0) continue;
                var fattore = 1m + perc / 100m;
                var nuovo = Math.Round(prezzo * fattore, 4);
                if (nuovo == prezzo) continue;
                string? campo = el == ElementoPrezzo.Sintetico ? CampoSintetico(r.Categoria) : null;
                var descrizione = el == ElementoPrezzo.Sintetico ? $"{Interfaccia.Testi.Categoria(r.Categoria)} (livello sintetico)" : r.Descrizione;
                righe.Add(new PropostaPrezzo($"VAR:{el}:{id:N}:{campo}", FontePrezzo.Variazione, r.PacchettoId, r.Pacchetto, r.Tariffa, r.Voce,
                    descrizione, r.Unita, r.Quantita, prezzo, nuovo, quota,
                    $"{(perc > 0 ? "+" : "")}{perc.ToString("0.##", CultureInfo.GetCultureInfo("it-IT"))}% {Interfaccia.Testi.Categoria(r.Categoria).ToLowerInvariant()}"
                    + (conRevisione.Contains(r.PacchettoId) ? " (il pacchetto ha anche una nuova revisione)" : ""))
                {
                    Elemento = el, ElementoId = id, Campo = campo,
                });
            }
        }

        return new PropostePrezzi(righe, tabellone.RicavoConCosto, tabellone.CostoPacchetti);
    }

    private static (CategoriaRisorsa, string, string, decimal, decimal, decimal) Firma(IRisorsa r) =>
        (r.Categoria, r.Descrizione.Trim(), (r.Unita ?? "").Trim().ToLowerInvariant(), r.Fabbisogno, r.SfridoPercentuale, r.Prezzo);

    private static bool StessoFornitore(Offerta a, Offerta b) =>
        (a.FornitoreId is not null && a.FornitoreId == b.FornitoreId)
        || (!string.IsNullOrWhiteSpace(a.NomeFornitore) && string.Equals(a.NomeFornitore.Trim(), b.NomeFornitore?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string CampoSintetico(CategoriaRisorsa c) => c switch
    {
        CategoriaRisorsa.Materiali => nameof(OffertaVoce.Fornitura),
        CategoriaRisorsa.Subappalti => nameof(OffertaVoce.FornituraEPosa),
        _ => nameof(OffertaVoce.ManodoperaENoli),
    };

    // ---- Conferma ----

    /// <summary>Applica le righe scelte (ricalcolate al momento della conferma) e registra l'aggiornamento con l'autore.</summary>
    public async Task<AggiornamentoPrezzi> ConfermaAsync(Guid commessaId, IReadOnlyCollection<string> scelte,
        IReadOnlyDictionary<CategoriaRisorsa, decimal>? variazioni, string? nota, CancellationToken ct = default)
    {
        if (!UtenteCorrente.PuoEmettereOrdini)
            throw new OperazioneNonValidaException("L'aggiornamento dei prezzi è riservato all'amministratore e agli operatori abilitati.");
        if (scelte.Count == 0) throw new OperazioneNonValidaException("Spunta almeno una riga da aggiornare.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var proposte = await ProposteAsync(db, commessaId, variazioni, ct);
        var perId = proposte.Righe.Where(r => !r.SoloMemoria).ToDictionary(r => r.Id);
        var mancanti = scelte.Where(id => !perId.ContainsKey(id)).ToList();
        if (mancanti.Count > 0)
            throw new OperazioneNonValidaException("Alcune righe non sono più attuali (prezzi cambiati nel frattempo): aggiorna la pagina e ripeti la scelta.");
        var righe = scelte.Select(id => perId[id]).ToList();
        var revisioni = righe.Where(r => r.Fonte == FontePrezzo.Revisione).Select(r => r.PacchettoId).ToHashSet();
        if (righe.FirstOrDefault(r => r.Fonte != FontePrezzo.Revisione && revisioni.Contains(r.PacchettoId)) is { } doppia)
            throw new OperazioneNonValidaException(
                $"Per il pacchetto «{doppia.Pacchetto}» hai scelto la nuova revisione dell'offerta: togli le altre righe dello stesso pacchetto, " +
                "che modificherebbero l'offerta sostituita.");

        var (_, mdcDopo, _) = proposte.Dopo(righe);
        var agg = new AggiornamentoPrezzi
        {
            CommessaId = commessaId, Data = orologio.GetUtcNow().UtcDateTime, Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim(),
            NumeroRighe = righe.Count, DifferenzaCosto = righe.Sum(r => r.DifferenzaCosto), MdcPrima = proposte.Mdc, MdcDopo = mdcDopo,
        };
        foreach (var r in righe)
        {
            var (prima, dopo) = await ApplicaAsync(db, commessaId, r, ct);
            agg.Righe.Add(new RigaAggiornamento
            {
                CommessaId = commessaId, Fonte = r.Fonte, PacchettoId = r.PacchettoId,
                Descrizione = Limita($"{r.Pacchetto} – {(r.Voce is null ? "" : r.Voce + " – ")}{r.Descrizione}", 1000),
                Elemento = r.Elemento, ElementoId = r.ElementoId, Campo = r.Campo,
                PrezzoPrima = r.PrezzoAttuale, PrezzoDopo = r.PrezzoNuovo, DifferenzaCosto = r.DifferenzaCosto, Prima = prima, Dopo = dopo,
            });
        }
        db.AggiornamentiPrezzi.Add(agg);
        db.InserimentiSoloInRiepilogo = true;
        db.NotaStorico = $"Aggiornamento prezzi: {righe.Count} righe";
        db.RegistraOperazione("Aggiornamento prezzi", nameof(AggiornamentoPrezzi), agg.Id, commessaId,
            $"{righe.Count} righe aggiornate, differenza di costo {Formato.Euro(agg.DifferenzaCosto)}, MDC da {Formato.Euro(agg.MdcPrima)} a {Formato.Euro(agg.MdcDopo)}"
            + (agg.Nota is null ? "" : $" – {agg.Nota}"),
            dopo: new { agg.NumeroRighe, DifferenzaCosto = Importi.Arrotonda(agg.DifferenzaCosto), MdcPrima = Importi.Arrotonda(agg.MdcPrima), MdcDopo = Importi.Arrotonda(agg.MdcDopo) });
        await db.SaveChangesAsync(ct);
        return agg;
    }

    private async Task<(string Prima, string Dopo)> ApplicaAsync(PreventivoDbContext db, Guid commessaId, PropostaPrezzo r, CancellationToken ct)
    {
        switch (r.Elemento)
        {
            case ElementoPrezzo.Risorsa:
            {
                var x = await db.RisorseVoci.FirstAsync(v => v.Id == r.ElementoId && v.CommessaId == commessaId, ct);
                var prima = x.Prezzo;
                x.Prezzo = r.PrezzoNuovo;
                return (Testo(prima), Testo(x.Prezzo));
            }
            case ElementoPrezzo.Sintetico:
            {
                var x = await db.OfferteVoci.FirstAsync(v => v.Id == r.ElementoId && v.CommessaId == commessaId, ct);
                var prima = LeggiCampo(x, r.Campo!);
                ScriviCampo(x, r.Campo!, r.PrezzoNuovo);
                return (Testo(prima), Testo(r.PrezzoNuovo));
            }
            case ElementoPrezzo.RigaOfferta:
            {
                var x = await db.RigheOfferta.FirstAsync(v => v.Id == r.ElementoId && v.CommessaId == commessaId, ct);
                var prima = x.PrezzoListino;
                // Il prezzo proposto è il netto: il listino varia nella stessa misura, lo sconto resta.
                x.PrezzoListino = r.PrezzoAttuale == 0 ? x.PrezzoListino : Math.Round(x.PrezzoListino * r.PrezzoNuovo / r.PrezzoAttuale, 4);
                return (Testo(prima), Testo(x.PrezzoListino));
            }
            case ElementoPrezzo.ACorpo:
            {
                var x = await db.CostiACorpo.FirstAsync(v => v.Id == r.ElementoId && v.CommessaId == commessaId, ct);
                var prima = x.Importo;
                x.Importo = Importi.Arrotonda(r.PrezzoNuovo);
                return (Testo(prima), Testo(x.Importo));
            }
            case ElementoPrezzo.RisorseAnalisi:
            {
                var ov = await db.OfferteVoci.Include(v => v.Risorse).FirstAsync(v => v.Id == r.ElementoId && v.CommessaId == commessaId, ct);
                var an = await db.AnalisiAziendali.AsNoTracking().Include(a => a.Righe).FirstAsync(a => a.Id == r.AnalisiId, ct);
                var prima = StatoRisorse(ov.Risorse);
                var elenco = ov.Risorse.OrderBy(x => x.Ordine).ToList();
                var posizione = elenco.FindIndex(x => x.AnalisiId == an.Id);
                foreach (var vecchia in elenco.Where(x => x.AnalisiId == an.Id).ToList())
                {
                    elenco.Remove(vecchia);
                    ov.Risorse.Remove(vecchia);
                    db.RisorseVoci.Remove(vecchia);
                }
                var fonte = $"Analisi {an.Codice ?? an.Descrizione}";
                var nuove = an.Righe.OrderBy(x => x.Ordine).Select(x => new RisorsaVoce
                {
                    OffertaVoceId = ov.Id, CommessaId = commessaId, Categoria = x.Categoria, Descrizione = x.Descrizione, Unita = x.Unita,
                    Fabbisogno = x.Fabbisogno, SfridoPercentuale = x.SfridoPercentuale, Prezzo = x.Prezzo, Fonte = x.Fonte ?? fonte, Note = x.Note,
                    AnalisiId = an.Id,
                }).ToList();
                elenco.InsertRange(Math.Max(0, posizione), nuove);
                foreach (var n in nuove) { ov.Risorse.Add(n); db.RisorseVoci.Add(n); }
                for (var i = 0; i < elenco.Count; i++) elenco[i].Ordine = i + 1;
                return (prima, StatoRisorse(elenco));
            }
            case ElementoPrezzo.Scelta:
            {
                var p = await db.Pacchetti.FirstAsync(x => x.Id == r.ElementoId && x.CommessaId == commessaId, ct);
                var prima = p.OffertaSceltaId;
                p.OffertaSceltaId = r.NuovaOffertaId;
                p.OffertaSceltaDa = utente.Nome;
                p.OffertaSceltaIl = orologio.GetUtcNow().UtcDateTime;
                return (prima?.ToString() ?? "", p.OffertaSceltaId?.ToString() ?? "");
            }
            default:
                throw new OperazioneNonValidaException("Riga non aggiornabile.");
        }
    }

    // ---- Storico e annullamento ----

    public async Task<IReadOnlyList<AggiornamentoPrezzi>> StoricoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.AggiornamentiPrezzi.AsNoTracking().Include(a => a.Righe).Where(a => a.CommessaId == commessaId)
            .OrderByDescending(a => a.Data).ToListAsync(ct);
    }

    /// <summary>
    /// Annulla in blocco un aggiornamento: ripristina i prezzi precedenti di tutte le sue righe, solo se nessuna è stata
    /// modificata di nuovo nel frattempo.
    /// </summary>
    public async Task AnnullaAsync(Guid commessaId, Guid aggiornamentoId, CancellationToken ct = default)
    {
        if (!UtenteCorrente.PuoEmettereOrdini)
            throw new OperazioneNonValidaException("L'annullamento di un aggiornamento dei prezzi è riservato all'amministratore e agli operatori abilitati.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var agg = await db.AggiornamentiPrezzi.Include(a => a.Righe).FirstOrDefaultAsync(a => a.Id == aggiornamentoId && a.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'aggiornamento non esiste.");
        if (agg.AnnullatoIl is not null) throw new OperazioneNonValidaException("L'aggiornamento è già stato annullato.");

        var cambiate = new List<string>();
        foreach (var r in agg.Righe)
            if (await StatoAttualeAsync(db, r, ct) != r.Dopo) cambiate.Add(r.Descrizione);
        if (cambiate.Count > 0)
            throw new OperazioneNonValidaException(
                $"Non si può annullare in blocco: {cambiate.Count} righe sono state modificate di nuovo dopo l'aggiornamento " +
                $"(ad esempio «{cambiate[0]}»). Correggi a mano le righe interessate.");

        foreach (var r in agg.Righe) await RipristinaAsync(db, r, ct);
        agg.AnnullatoIl = orologio.GetUtcNow().UtcDateTime;
        agg.AnnullatoDa = utente.Nome;
        db.NotaStorico = "Annullamento dell'aggiornamento prezzi";
        db.RegistraOperazione("Annullamento aggiornamento prezzi", nameof(AggiornamentoPrezzi), agg.Id, commessaId,
            $"{agg.NumeroRighe} righe riportate ai prezzi precedenti (aggiornamento del {agg.Data.ToLocalTime():dd/MM/yyyy HH:mm})");
        await db.SaveChangesAsync(ct);
    }

    private static async Task<string?> StatoAttualeAsync(PreventivoDbContext db, RigaAggiornamento r, CancellationToken ct) => r.Elemento switch
    {
        ElementoPrezzo.Risorsa => (await db.RisorseVoci.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ElementoId, ct)) is { } x ? Testo(x.Prezzo) : null,
        ElementoPrezzo.Sintetico => (await db.OfferteVoci.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ElementoId, ct)) is { } x ? Testo(LeggiCampo(x, r.Campo!)) : null,
        ElementoPrezzo.RigaOfferta => (await db.RigheOfferta.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ElementoId, ct)) is { } x ? Testo(x.PrezzoListino) : null,
        ElementoPrezzo.ACorpo => (await db.CostiACorpo.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ElementoId, ct)) is { } x ? Testo(x.Importo) : null,
        ElementoPrezzo.RisorseAnalisi => (await db.OfferteVoci.AsNoTracking().Include(v => v.Risorse).FirstOrDefaultAsync(x => x.Id == r.ElementoId, ct)) is { } x
            ? StatoRisorse(x.Risorse) : null,
        ElementoPrezzo.Scelta => (await db.Pacchetti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ElementoId, ct)) is { } x ? x.OffertaSceltaId?.ToString() ?? "" : null,
        _ => null,
    };

    private async Task RipristinaAsync(PreventivoDbContext db, RigaAggiornamento r, CancellationToken ct)
    {
        switch (r.Elemento)
        {
            case ElementoPrezzo.Risorsa:
                (await db.RisorseVoci.FirstAsync(x => x.Id == r.ElementoId, ct)).Prezzo = Valore(r.Prima);
                break;
            case ElementoPrezzo.Sintetico:
                ScriviCampo(await db.OfferteVoci.FirstAsync(x => x.Id == r.ElementoId, ct), r.Campo!, Valore(r.Prima));
                break;
            case ElementoPrezzo.RigaOfferta:
                (await db.RigheOfferta.FirstAsync(x => x.Id == r.ElementoId, ct)).PrezzoListino = Valore(r.Prima);
                break;
            case ElementoPrezzo.ACorpo:
                (await db.CostiACorpo.FirstAsync(x => x.Id == r.ElementoId, ct)).Importo = Valore(r.Prima);
                break;
            case ElementoPrezzo.RisorseAnalisi:
            {
                var ov = await db.OfferteVoci.Include(v => v.Risorse).FirstAsync(x => x.Id == r.ElementoId, ct);
                db.RisorseVoci.RemoveRange(ov.Risorse);
                ov.Risorse.Clear();
                foreach (var s in JsonSerializer.Deserialize<List<StatoRisorsa>>(r.Prima, Json) ?? [])
                {
                    var n = new RisorsaVoce
                    {
                        OffertaVoceId = ov.Id, CommessaId = ov.CommessaId, Ordine = s.Ordine, Categoria = s.Categoria, Descrizione = s.Descrizione,
                        Unita = s.Unita, Fabbisogno = s.Fabbisogno, SfridoPercentuale = s.SfridoPercentuale, Prezzo = s.Prezzo, Fonte = s.Fonte,
                        Note = s.Note, AnalisiId = s.AnalisiId,
                    };
                    ov.Risorse.Add(n);
                    db.RisorseVoci.Add(n);
                }
                break;
            }
            case ElementoPrezzo.Scelta:
            {
                var p = await db.Pacchetti.FirstAsync(x => x.Id == r.ElementoId, ct);
                p.OffertaSceltaId = Guid.TryParse(r.Prima, out var id) ? id : null;
                p.OffertaSceltaDa = utente.Nome;
                p.OffertaSceltaIl = orologio.GetUtcNow().UtcDateTime;
                break;
            }
        }
    }

    private static string StatoRisorse(IEnumerable<RisorsaVoce> risorse) =>
        JsonSerializer.Serialize(risorse.OrderBy(r => r.Ordine).Select(r => new StatoRisorsa(r.Ordine, r.Categoria, r.Descrizione, r.Unita,
            Norma(r.Fabbisogno), Norma(r.SfridoPercentuale), Norma(r.Prezzo), r.Fonte, r.Note, r.AnalisiId)).ToList(), Json);

    /// <summary>Stesso valore con la stessa scala, letto dal database o calcolato: i confronti tra stati non dipendono dagli zeri finali.</summary>
    private static decimal Norma(decimal v) => Valore(Testo(v));

    private static decimal LeggiCampo(OffertaVoce v, string campo) => campo switch
    {
        nameof(OffertaVoce.Fornitura) => v.Fornitura,
        nameof(OffertaVoce.FornituraEPosa) => v.FornituraEPosa,
        _ => v.ManodoperaENoli,
    };

    private static void ScriviCampo(OffertaVoce v, string campo, decimal valore)
    {
        switch (campo)
        {
            case nameof(OffertaVoce.Fornitura): v.Fornitura = valore; break;
            case nameof(OffertaVoce.FornituraEPosa): v.FornituraEPosa = valore; break;
            default: v.ManodoperaENoli = valore; break;
        }
    }

    private static string Testo(decimal v) => v.ToString("0.############", CultureInfo.InvariantCulture);
    private static decimal Valore(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
    private static string Limita(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
