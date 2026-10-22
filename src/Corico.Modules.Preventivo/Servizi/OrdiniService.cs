using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Dati proposti per un nuovo ordine, modificabili prima dell'emissione (solo testata).</summary>
public sealed class DatiEmissione
{
    public DateOnly Data { get; set; }
    public string Oggetto { get; set; } = "";
    public string? LuogoConsegna { get; set; }
    public string? Consegna { get; set; }
    public string? Pagamento { get; set; }
    public string? Note { get; set; }
}

/// <summary>Anteprima dell'ordine: righe ricavate dall'offerta scelta e motivi che impediscono l'emissione.</summary>
public sealed record AnteprimaOrdine(
    DatiEmissione Proposta, string Fornitore, string? DatiFornitore, string? RiferimentoOfferta,
    IReadOnlyList<RigaOrdine> Righe, IReadOnlyList<string> Impedimenti, bool AziendaCompilata)
{
    public decimal Totale => Righe.Sum(r => r.Importo);
    public bool Emettibile => Impedimenti.Count == 0;
}

public sealed record RigaElencoOrdini(
    Guid Id, string Numero, DateOnly Data, string Fornitore, string Oggetto, Guid PacchettoId, string Pacchetto,
    decimal Totale, StatoOrdine Stato, string? MotivoAnnullamento, DateOnly? AnnullatoIl);

/// <summary>
/// Ordini di acquisto ai fornitori (richiesta del 22/10/2026): emessi dal pacchetto con l'offerta scelta,
/// numerati per anno, immutabili; si annullano con un motivo e se ne emette uno nuovo.
/// </summary>
public sealed class OrdiniService(
    IDbContextFactory<PreventivoDbContext> fabbrica, CommesseService commesse, FornitoriService fornitori,
    ImpostazioniService impostazioni, TimeProvider orologio)
{
    private static readonly SemaphoreSlim BloccoNumeri = new(1, 1);

    public async Task<IReadOnlyList<RigaElencoOrdini>> ElencoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var pacchetti = await db.Pacchetti.AsNoTracking().Where(p => p.CommessaId == commessaId)
            .ToDictionaryAsync(p => p.Id, p => p.Descrizione, ct);
        var ordini = await db.Ordini.AsNoTracking().Where(o => o.CommessaId == commessaId).ToListAsync(ct);
        return ordini.OrderByDescending(o => o.Anno).ThenByDescending(o => o.Progressivo)
            .Select(o => new RigaElencoOrdini(o.Id, o.Numero, o.Data, o.Fornitore, o.Oggetto, o.PacchettoId,
                pacchetti.GetValueOrDefault(o.PacchettoId, ""), o.Totale, o.Stato, o.MotivoAnnullamento, o.AnnullatoIl))
            .ToList();
    }

    public async Task<IReadOnlyList<OrdineAcquisto>> PerPacchettoAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Ordini.AsNoTracking().Where(o => o.CommessaId == commessaId && o.PacchettoId == pacchettoId)
            .OrderByDescending(o => o.Anno).ThenByDescending(o => o.Progressivo).ToListAsync(ct);
    }

    public async Task<OrdineAcquisto?> OttieniAsync(Guid commessaId, Guid ordineId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.AsNoTracking().Include(x => x.Righe)
            .FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct);
        o?.Righe.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        return o;
    }

    /// <summary>Ordine proposto dall'offerta scelta del pacchetto, con gli eventuali impedimenti all'emissione.</summary>
    public async Task<AnteprimaOrdine?> AnteprimaAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await AnteprimaAsync(db, commessaId, pacchettoId, ct);
    }

    private async Task<AnteprimaOrdine?> AnteprimaAsync(PreventivoDbContext db, Guid commessaId, Guid pacchettoId, CancellationToken ct)
    {
        var p = await db.Pacchetti.AsNoTracking().Include(x => x.Voci)
            .FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct);
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        if (p is null || commessa is null) return null;

        var impedimenti = new List<string>();
        var azienda = await impostazioni.LeggiAziendaAsync(ct);
        if (await db.Ordini.AnyAsync(o => o.PacchettoId == pacchettoId && o.Stato == StatoOrdine.Emesso, ct))
            impedimenti.Add("Per questo pacchetto c'è già un ordine emesso: per sostituirlo lo annulli prima.");

        var offerta = p.OffertaSceltaId is { } sceltaId
            ? (await PacchettiService.CaricaOfferteAsync(db, commessaId, ct)).FirstOrDefault(o => o.Id == sceltaId)
            : null;
        var proposta = new DatiEmissione
        {
            Data = DateOnly.FromDateTime(orologio.GetLocalNow().DateTime),
            Oggetto = $"Commessa {commessa.Codice} – {p.Descrizione}",
            LuogoConsegna = Cantiere(commessa),
            Consegna = offerta?.Consegna,
            Pagamento = offerta?.Pagamento,
        };
        if (offerta is null)
        {
            impedimenti.Add("Il pacchetto non ha un'offerta scelta.");
            return new AnteprimaOrdine(proposta, "", null, null, [], impedimenti, azienda.Compilati);
        }
        if (offerta.Tipo == TipoOfferta.StimaInterna)
            impedimenti.Add("L'offerta scelta è una stima interna: per emettere l'ordine scelga l'offerta di un fornitore.");

        DatiFornitore? fornitore = offerta.FornitoreId is { } fid ? await fornitori.OttieniAsync(fid, ct) : null;
        var nome = fornitore?.Denominazione ?? offerta.NomeFornitore ?? "";
        if (string.IsNullOrWhiteSpace(nome))
            impedimenti.Add("L'offerta scelta non indica il fornitore.");

        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var voci = PacchettiService.VociDel(p, await PacchettiService.VociDiCalcoloAsync(db, commessaId, ct));
        var righe = Righe(offerta, CalcoloPacchetti.Calcola(offerta, voci, preventivo.RibassoPercentuale), commessaId);
        if (righe.Count == 0 || righe.All(r => r.Importo == 0))
            impedimenti.Add("L'offerta scelta non ha importi da ordinare.");

        return new AnteprimaOrdine(proposta, nome.Trim(), fornitore is null ? null : DatiDel(fornitore),
            RiferimentoOfferta(offerta), righe, impedimenti, azienda.Compilati);
    }

    /// <summary>
    /// Emette l'ordine: numero progressivo dell'anno, righe dall'offerta scelta, condizioni generali dell'impresa.
    /// Il pacchetto passa a costo Definitivo con il numero d'ordine come riferimento ODA.
    /// </summary>
    public async Task<OrdineAcquisto> EmettiAsync(Guid commessaId, Guid pacchettoId, DatiEmissione dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Oggetto))
            throw new OperazioneNonValidaException("L'oggetto dell'ordine è obbligatorio.");

        await BloccoNumeri.WaitAsync(ct);
        try
        {
            await using var db = await fabbrica.CreateDbContextAsync(ct);
            var a = await AnteprimaAsync(db, commessaId, pacchettoId, ct)
                ?? throw new OperazioneNonValidaException("Il pacchetto non esiste.");
            if (!a.Emettibile) throw new OperazioneNonValidaException(a.Impedimenti[0]);

            var p = await db.Pacchetti.FirstAsync(x => x.Id == pacchettoId, ct);
            var offerta = await db.Offerte.AsNoTracking().FirstAsync(o => o.Id == p.OffertaSceltaId, ct);
            var azienda = await impostazioni.LeggiAziendaAsync(ct);
            var anno = dati.Data.Year;
            var progressivo = (await db.Ordini.Where(o => o.Anno == anno).MaxAsync(o => (int?)o.Progressivo, ct) ?? 0) + 1;

            var ordine = new OrdineAcquisto
            {
                CommessaId = commessaId,
                PacchettoId = pacchettoId,
                OffertaId = offerta.Id,
                Anno = anno,
                Progressivo = progressivo,
                Numero = OrdineAcquisto.FormattaNumero(anno, progressivo),
                Data = dati.Data,
                FornitoreId = offerta.FornitoreId,
                Fornitore = a.Fornitore,
                DatiFornitore = a.DatiFornitore,
                Oggetto = dati.Oggetto.Trim(),
                RiferimentoOfferta = a.RiferimentoOfferta,
                Pagamento = Pulisci(dati.Pagamento),
                Consegna = Pulisci(dati.Consegna),
                LuogoConsegna = Pulisci(dati.LuogoConsegna),
                Note = Pulisci(dati.Note),
                Condizioni = Pulisci(azienda.CondizioniOrdine),
                Totale = a.Totale,
                Stato = StatoOrdine.Emesso,
            };
            foreach (var r in a.Righe)
            {
                r.OrdineId = ordine.Id;
                ordine.Righe.Add(r);
            }
            db.Ordini.Add(ordine);

            p.StatoCosto = StatoCosto.Definitivo;
            p.RiferimentoOda = ordine.Numero;

            db.InserimentiSoloInRiepilogo = true;
            db.RegistraOperazione("Emissione ordine", nameof(OrdineAcquisto), ordine.Id, commessaId,
                $"{ordine.Numero} a {ordine.Fornitore}: {Formato.Euro(ordine.Totale)} (pacchetto {p.Descrizione})",
                dopo: new { ordine.Numero, ordine.Fornitore, Totale = ordine.Totale, Righe = ordine.Righe.Count });
            await db.SaveChangesAsync(ct);
            return ordine;
        }
        finally
        {
            BloccoNumeri.Release();
        }
    }

    /// <summary>Annulla un ordine emesso. Il pacchetto torna a costo Provvisorio se aveva questo ordine come riferimento.</summary>
    public async Task AnnullaAsync(Guid commessaId, Guid ordineId, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new OperazioneNonValidaException("Il motivo dell'annullamento è obbligatorio.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'ordine non esiste.");
        if (o.Stato == StatoOrdine.Annullato)
            throw new OperazioneNonValidaException("L'ordine è già annullato.");

        o.Stato = StatoOrdine.Annullato;
        o.MotivoAnnullamento = motivo.Trim();
        o.AnnullatoIl = DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);
        var p = await db.Pacchetti.FirstOrDefaultAsync(x => x.Id == o.PacchettoId, ct);
        if (p is not null && p.RiferimentoOda == o.Numero)
        {
            p.RiferimentoOda = null;
            if (p.StatoCosto == StatoCosto.Definitivo) p.StatoCosto = StatoCosto.Provvisorio;
        }
        db.NotaStorico = $"Annullamento dell'ordine {o.Numero}: {o.MotivoAnnullamento}";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Righe dell'ordine: per ogni voce il costo unitario dell'offerta (tre colonne o risorse), poi le righe d'offerta
    /// al prezzo netto e le voci a corpo. Il totale coincide con il costo del pacchetto, salvo arrotondamenti al centesimo.
    /// </summary>
    internal static List<RigaOrdine> Righe(Offerta offerta, RisultatoOfferta risultato, Guid commessaId)
    {
        var righe = new List<RigaOrdine>();
        void Aggiungi(string? codice, string descrizione, string? unita, decimal quantita, decimal prezzo, decimal importo) =>
            righe.Add(new RigaOrdine
            {
                CommessaId = commessaId, Ordine = righe.Count + 1, Codice = codice, Descrizione = descrizione.Trim(),
                Unita = unita, Quantita = quantita, Prezzo = prezzo, Importo = importo,
            });

        var righeOfferta = offerta.Righe.OrderBy(r => r.Ordine).ToList();
        foreach (var v in risultato.Voci)
        {
            if (v.CostoUnitarioSintetico != 0)
                Aggiungi(v.Voce.Tariffa, v.Voce.Descrizione, v.Voce.Unita, v.Quantita, v.CostoUnitarioSintetico,
                    Importi.Arrotonda(v.Quantita * v.CostoUnitarioSintetico));
            foreach (var r in righeOfferta.Where(r => r.VoceComputoId == v.Voce.Id))
                AggiungiRiga(r);
        }
        var idVoci = risultato.Voci.Select(v => v.Voce.Id).ToHashSet();
        foreach (var r in righeOfferta.Where(r => r.VoceComputoId is null || !idVoci.Contains(r.VoceComputoId.Value)))
            AggiungiRiga(r);
        foreach (var c in offerta.CostiACorpo.OrderBy(c => c.Ordine))
            Aggiungi(null, c.Incluso ? c.Descrizione + " (incluso)" : c.Descrizione, "a corpo", 1, c.Importo, c.Incluso ? 0 : c.Importo);
        return righe;

        void AggiungiRiga(RigaOfferta r) =>
            Aggiungi(r.CodiceArticolo, r.Posizione is null ? r.Descrizione : $"{r.Descrizione} (pos. {r.Posizione})", r.Unita, r.Quantita,
                Importi.Arrotonda(CalcoloPacchetti.PrezzoNetto(r.PrezzoListino, r.ScontoPercentuale), 4),
                CalcoloPacchetti.ImportoRiga(r.Quantita, r.PrezzoListino, r.ScontoPercentuale));
    }

    private static string? Cantiere(DatiCommessa c)
    {
        var s = string.Join(" ", new[] { c.IndirizzoCantiere, c.ComuneCantiere, c.ProvinciaCantiere is { Length: > 0 } pr ? $"({pr})" : null }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return s.Length == 0 ? null : "Cantiere: " + s;
    }

    private static string DatiDel(DatiFornitore f) =>
        string.Join("\n", new[]
        {
            string.Join(" ", new[] { f.Indirizzo, f.Comune, f.Provincia is { Length: > 0 } pr ? $"({pr})" : null }.Where(x => !string.IsNullOrWhiteSpace(x))),
            string.Join(" · ", new[] { f.PartitaIva is { Length: > 0 } pi ? "P. IVA " + pi : null, f.CodiceFiscale is { Length: > 0 } cf ? "C.F. " + cf : null }.Where(x => x is not null)),
            string.Join(" · ", new[] { f.Telefono is { Length: > 0 } t ? "Tel. " + t : null, f.Email }.Where(x => !string.IsNullOrWhiteSpace(x))),
        }.Where(x => x.Length > 0));

    private static string? RiferimentoOfferta(Offerta o)
    {
        var s = string.Join(" ", new[]
        {
            o.Numero is null ? null : "n. " + o.Numero,
            o.Revisione is null ? null : "rev. " + o.Revisione,
            o.Data is null ? null : "del " + Formato.Data(o.Data),
        }.Where(x => x is not null));
        return s.Length == 0 ? null : "Vs. offerta " + s;
    }

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
