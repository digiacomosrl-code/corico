using ClosedXML.Excel;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record RigaElencoRichieste(
    Guid Id, string Numero, DateOnly Data, string Oggetto, string? Pacchetto, DateOnly? Scadenza, int Righe,
    IReadOnlyList<DestinatarioRichiesta> Destinatari, string Autore)
{
    /// <summary>Fornitori che non hanno ancora risposto (né offerta né rinuncia).</summary>
    public int SenzaRisposta => Destinatari.Count(d => d.Stato is StatoRisposta.DaInviare or StatoRisposta.Inviata);

    public bool Scaduta(DateOnly oggi) => Scadenza is { } s && s < oggi && SenzaRisposta > 0;
}

/// <summary>Prezzi letti dall'Excel della richiesta compilato dal fornitore, per riga della richiesta.</summary>
public sealed record PrezziCompilati(IReadOnlyDictionary<Guid, (decimal Prezzo, decimal Sconto)> Righe, string? Validita,
    string? Pagamento, string? Consegna, string? Esclusioni, string? Numero);

/// <summary>
/// Richieste d'offerta (Modulo 2, rilascio 2B): si preparano dal pacchetto o da righe della Lista della Spesa e si indirizzano a
/// uno o più fornitori. Non contengono prezzi. Alla risposta «Registra offerta» crea l'offerta nel pacchetto con le righe già
/// compilate dalle voci della richiesta (e i prezzi, se il fornitore ha compilato l'Excel della richiesta).
/// </summary>
public sealed class RichiesteService(IDbContextFactory<PreventivoDbContext> fabbrica, FornitoriService fornitori, TimeProvider orologio)
{
    private static readonly SemaphoreSlim BloccoNumeri = new(1, 1);

    public static string FormattaNumero(int anno, int progressivo) => $"RdO {anno}/{progressivo:000}";

    private DateOnly Oggi => DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<RigaElencoRichieste>> ElencoAsync(Guid commessaId, Guid? pacchettoId = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var pacchetti = await db.Pacchetti.AsNoTracking().Where(p => p.CommessaId == commessaId).ToDictionaryAsync(p => p.Id, p => p.Descrizione, ct);
        var richieste = await db.Richieste.AsNoTracking().AsSplitQuery().Include(r => r.Righe).Include(r => r.Destinatari)
            .Where(r => r.CommessaId == commessaId).ToListAsync(ct);
        return richieste
            .Where(r => pacchettoId is null || r.PacchettoId == pacchettoId || r.Righe.Any(x => x.PacchettoId == pacchettoId))
            .OrderByDescending(r => r.Anno).ThenByDescending(r => r.Progressivo)
            .Select(r => new RigaElencoRichieste(r.Id, r.Numero, r.Data, r.Oggetto,
                string.Join(", ", PacchettiDi(r).Select(id => pacchetti.GetValueOrDefault(id, "")).Where(n => n.Length > 0)) is { Length: > 0 } n ? n : null,
                r.Scadenza, r.Righe.Count, r.Destinatari.OrderBy(d => d.Fornitore).ToList(), r.CreatoDa))
            .ToList();
    }

    public async Task<RichiestaOfferta?> OttieniAsync(Guid commessaId, Guid richiestaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var r = await db.Richieste.AsNoTracking().AsSplitQuery().Include(x => x.Righe).Include(x => x.Destinatari)
            .FirstOrDefaultAsync(x => x.Id == richiestaId && x.CommessaId == commessaId, ct);
        r?.Righe.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        r?.Destinatari.Sort((a, b) => string.Compare(a.Fornitore, b.Fornitore, StringComparison.CurrentCultureIgnoreCase));
        return r;
    }

    public static IEnumerable<Guid> PacchettiDi(RichiestaOfferta r) =>
        new[] { r.PacchettoId }.Concat(r.Righe.Select(x => x.PacchettoId)).Where(x => x is not null).Select(x => x!.Value).Distinct();

    // ---- Preparazione ----

    /// <summary>Richiesta con tutte le voci del pacchetto: descrizione estesa, unità e quantità del computo.</summary>
    public async Task<Guid> CreaDaPacchettoAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.AsNoTracking().Include(x => x.Voci).FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il pacchetto non esiste.");
        var ids = p.Voci.Select(v => v.VoceComputoId).ToList();
        var voci = await db.VociComputo.AsNoTracking().Include(v => v.VoceElencoPrezzi)
            .Where(v => ids.Contains(v.Id) && !v.Archiviata).OrderBy(v => v.Ordine).ToListAsync(ct);
        if (voci.Count == 0) throw new OperazioneNonValidaException("Il pacchetto non ha voci da richiedere: assegnagli prima le voci del computo.");

        var r = Nuova(commessaId, p.Descrizione);
        r.PacchettoId = p.Id;
        var ordine = 0;
        foreach (var v in voci)
            r.Righe.Add(new RigaRichiesta
            {
                CommessaId = commessaId, Ordine = ++ordine, PacchettoId = p.Id, VoceComputoId = v.Id, Codice = v.Tariffa,
                Descrizione = DescrizioneEstesa(v), Unita = v.Unita, Quantita = v.Quantita,
            });
        // Propone come destinatari i fornitori delle offerte già presenti nel pacchetto.
        var offerte = await db.Offerte.AsNoTracking().Where(o => o.PacchettoId == p.Id && o.Tipo != TipoOfferta.StimaInterna && o.NomeFornitore != null)
            .Select(o => new { o.FornitoreId, o.NomeFornitore }).ToListAsync(ct);
        foreach (var o in offerte.DistinctBy(o => o.NomeFornitore!.Trim().ToLowerInvariant()))
            r.Destinatari.Add(new DestinatarioRichiesta { CommessaId = commessaId, FornitoreId = o.FornitoreId, Fornitore = o.NomeFornitore!.Trim() });
        return await SalvaNuovaAsync(db, r, ct);
    }

    /// <summary>
    /// Richiesta dalle righe selezionate della Lista della Spesa (per chiave): la stessa risorsa di più voci diventa una riga con la
    /// quantità totale prevista, come nella vista aggregata.
    /// </summary>
    public async Task<Guid> CreaDaListaAsync(Guid commessaId, IReadOnlyCollection<string> chiavi, CancellationToken ct = default)
    {
        if (chiavi.Count == 0) throw new OperazioneNonValidaException("Seleziona almeno una riga della Lista della Spesa.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        var set = chiavi.ToHashSet();
        var selezionate = lista.Righe.Where(r => set.Contains(r.ChiaveOrdine)).ToList();
        if (selezionate.Count == 0)
            throw new OperazioneNonValidaException("Le righe selezionate non sono più nella Lista della Spesa: aggiorna la pagina.");

        var pacchetti = selezionate.Select(s => s.PacchettoId).Distinct().ToList();
        var oggetto = pacchetti.Count == 1 ? selezionate[0].Pacchetto : string.Join(", ", selezionate.Select(s => Interfaccia.Testi.Categoria(s.Categoria)).Distinct());
        var r = Nuova(commessaId, oggetto);
        if (pacchetti.Count == 1) r.PacchettoId = pacchetti[0];
        var ordine = 0;
        foreach (var g in selezionate.GroupBy(s => (s.Categoria, s.Chiave)))
        {
            var primo = g.First();
            var stessoPacchetto = g.Select(x => x.PacchettoId).Distinct().Count() == 1;
            var voci = g.Select(x => x.VoceId).Distinct().ToList();
            r.Righe.Add(new RigaRichiesta
            {
                CommessaId = commessaId, Ordine = ++ordine, Codice = primo.Codice, Descrizione = primo.Descrizione.Trim(), Unita = primo.Unita,
                Quantita = g.Sum(x => x.Quantita),
                PacchettoId = stessoPacchetto ? primo.PacchettoId : null,
                VoceComputoId = stessoPacchetto && voci.Count == 1 ? voci[0] : null,
                Chiavi = string.Join(";", g.Select(x => x.ChiaveOrdine)),
            });
        }
        foreach (var f in selezionate.Where(s => !s.FornitoreLibero).GroupBy(s => s.Fornitore!.Trim().ToLowerInvariant()))
            r.Destinatari.Add(new DestinatarioRichiesta { CommessaId = commessaId, FornitoreId = f.First().FornitoreId, Fornitore = f.First().Fornitore!.Trim() });
        return await SalvaNuovaAsync(db, r, ct);
    }

    private RichiestaOfferta Nuova(Guid commessaId, string oggetto) => new()
    {
        CommessaId = commessaId, Data = Oggi, Oggetto = oggetto.Trim(), Scadenza = Oggi.AddDays(10),
    };

    /// <summary>Il numero si assegna subito: una richiesta parte verso i fornitori, a differenza della bozza d'ordine.</summary>
    private static async Task<Guid> SalvaNuovaAsync(PreventivoDbContext db, RichiestaOfferta r, CancellationToken ct)
    {
        await BloccoNumeri.WaitAsync(ct);
        try
        {
            r.Anno = r.Data.Year;
            r.Progressivo = (await db.Richieste.Where(x => x.Anno == r.Anno).MaxAsync(x => (int?)x.Progressivo, ct) ?? 0) + 1;
            r.Numero = FormattaNumero(r.Anno, r.Progressivo);
            db.Richieste.Add(r);
            db.InserimentiSoloInRiepilogo = true;
            db.RegistraOperazione("Richiesta d'offerta", nameof(RichiestaOfferta), r.Id, r.CommessaId,
                $"{r.Numero}: {r.Oggetto} ({r.Righe.Count} righe, {r.Destinatari.Count} fornitori)");
            await db.SaveChangesAsync(ct);
            return r.Id;
        }
        finally
        {
            BloccoNumeri.Release();
        }
    }

    // ---- Modifica ----

    /// <summary>Salva testata, righe (descrizione, unità, quantità) e destinatari.</summary>
    public async Task SalvaAsync(Guid commessaId, RichiestaOfferta dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Oggetto)) throw new OperazioneNonValidaException("L'oggetto della richiesta è obbligatorio.");
        if (dati.Righe.Count == 0) throw new OperazioneNonValidaException("La richiesta deve avere almeno una riga.");
        if (dati.Righe.Any(r => string.IsNullOrWhiteSpace(r.Descrizione)))
            throw new OperazioneNonValidaException("Ogni riga della richiesta deve avere una descrizione.");
        if (dati.Righe.Any(r => r.Quantita < 0)) throw new OperazioneNonValidaException("Le quantità non possono essere negative.");
        if (dati.Destinatari.Any(d => string.IsNullOrWhiteSpace(d.Fornitore)))
            throw new OperazioneNonValidaException("Indica il fornitore di ogni destinatario.");
        if (dati.Destinatari.GroupBy(d => d.Fornitore.Trim().ToLowerInvariant()).Any(g => g.Count() > 1))
            throw new OperazioneNonValidaException("Lo stesso fornitore compare due volte tra i destinatari.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var r = await db.Richieste.AsSplitQuery().Include(x => x.Righe).Include(x => x.Destinatari)
            .FirstOrDefaultAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La richiesta non esiste più.");
        db.Entry(r).Property(x => x.Versione).OriginalValue = dati.Versione;
        r.Oggetto = dati.Oggetto.Trim();
        r.Data = dati.Data;
        r.Scadenza = dati.Scadenza;
        r.LuogoLavori = Pulisci(dati.LuogoLavori);
        r.Note = Pulisci(dati.Note);

        var ordine = 0;
        Sincronizza(db, r.Righe, dati.Righe, (a, b) =>
        {
            a.Ordine = ++ordine;
            a.Codice = Pulisci(b.Codice);
            a.Descrizione = b.Descrizione.Trim();
            a.Unita = Importazione.UnitaMisura.Normalizza(b.Unita);
            a.Quantita = b.Quantita;
            a.PacchettoId = b.PacchettoId;
            a.VoceComputoId = b.VoceComputoId;
            a.Chiavi = b.Chiavi;
        }, commessaId, x => x.RichiestaId = r.Id);
        Sincronizza(db, r.Destinatari, dati.Destinatari, (a, b) =>
        {
            a.FornitoreId = b.FornitoreId;
            a.Fornitore = b.Fornitore.Trim();
            a.Stato = b.Stato;
            a.InviataIl = b.InviataIl ?? (b.Stato != StatoRisposta.DaInviare && a.InviataIl is null ? Oggi : a.InviataIl);
            a.RispostaIl = b.Stato is StatoRisposta.Ricevuta or StatoRisposta.Declinata ? b.RispostaIl ?? a.RispostaIl ?? Oggi : null;
            a.Note = Pulisci(b.Note);
        }, commessaId, x => x.RichiestaId = r.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Segna la richiesta come inviata ai fornitori ancora «da inviare» (dopo aver spedito PDF o Excel dalla propria posta).</summary>
    public async Task SegnaInviataAsync(Guid commessaId, Guid richiestaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var dest = await db.DestinatariRichiesta.Where(d => d.RichiestaId == richiestaId && d.CommessaId == commessaId && d.Stato == StatoRisposta.DaInviare)
            .ToListAsync(ct);
        foreach (var d in dest) (d.Stato, d.InviataIl) = (StatoRisposta.Inviata, Oggi);
        await db.SaveChangesAsync(ct);
    }

    public async Task EliminaAsync(Guid commessaId, Guid richiestaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var r = await db.Richieste.Include(x => x.Destinatari).FirstOrDefaultAsync(x => x.Id == richiestaId && x.CommessaId == commessaId, ct);
        if (r is null) return;
        if (r.Destinatari.Any(d => d.OffertaId is not null))
            throw new OperazioneNonValidaException("La richiesta ha già offerte registrate: non si può eliminare.");
        db.Richieste.Remove(r);
        db.NotaStorico = $"Richiesta d'offerta eliminata: {r.Numero} {r.Oggetto}";
        await db.SaveChangesAsync(ct);
    }

    // ---- Risposta del fornitore ----

    /// <summary>
    /// Crea l'offerta del fornitore nel pacchetto, con una riga per ogni riga della richiesta che riguarda il pacchetto (quantità,
    /// unità e collegamento alla voce) e i prezzi, se il fornitore ha compilato l'Excel della richiesta. Il destinatario diventa «Ricevuta».
    /// </summary>
    public async Task<(Guid OffertaId, Guid PacchettoId, int Prezzi)> RegistraOffertaAsync(Guid commessaId, Guid destinatarioId, Guid? pacchettoId,
        PrezziCompilati? compilati = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var d = await db.DestinatariRichiesta.FirstOrDefaultAsync(x => x.Id == destinatarioId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il destinatario non esiste più.");
        if (d.OffertaId is not null && await db.Offerte.AnyAsync(o => o.Id == d.OffertaId, ct))
            throw new OperazioneNonValidaException($"L'offerta di {d.Fornitore} è già registrata: aprila dal pacchetto.");
        var r = await db.Richieste.AsNoTracking().Include(x => x.Righe).FirstAsync(x => x.Id == d.RichiestaId, ct);
        var candidati = PacchettiDi(r).ToList();
        var pid = pacchettoId ?? (candidati.Count == 1 ? candidati[0]
            : throw new OperazioneNonValidaException("La richiesta riguarda più pacchetti: scegli il pacchetto a cui registrare l'offerta."));
        var p = await db.Pacchetti.Include(x => x.Voci).FirstOrDefaultAsync(x => x.Id == pid && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il pacchetto non esiste.");
        var vociPacchetto = p.Voci.Select(v => v.VoceComputoId).ToHashSet();

        var o = new Offerta
        {
            PacchettoId = p.Id, CommessaId = commessaId, FornitoreId = d.FornitoreId, NomeFornitore = d.Fornitore,
            Tipo = TipoDa(p.Tipo), Data = Oggi, Note = $"Risposta alla {r.Numero}",
            Numero = Pulisci(compilati?.Numero), Pagamento = Pulisci(compilati?.Pagamento), Consegna = Pulisci(compilati?.Consegna),
            Esclusioni = Pulisci(compilati?.Esclusioni),
        };
        if (int.TryParse(compilati?.Validita?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var giorni) && giorni > 0)
        {
            o.ValiditaGiorni = giorni;
            o.Scadenza = Oggi.AddDays(giorni);
        }
        foreach (var v in vociPacchetto) o.Voci.Add(new OffertaVoce { CommessaId = commessaId, VoceComputoId = v });
        var ordine = 0;
        var prezzi = 0;
        foreach (var riga in r.Righe.Where(x => x.PacchettoId == p.Id || (x.PacchettoId is null && candidati.Count <= 1)).OrderBy(x => x.Ordine))
        {
            var ro = new RigaOfferta
            {
                CommessaId = commessaId, Ordine = ++ordine, CodiceArticolo = riga.Codice, Descrizione = riga.Descrizione, Unita = riga.Unita,
                Quantita = riga.Quantita, VoceComputoId = riga.VoceComputoId is { } vid && vociPacchetto.Contains(vid) ? vid : null,
            };
            if (compilati?.Righe.TryGetValue(riga.Id, out var pr) == true)
            {
                (ro.PrezzoListino, ro.ScontoPercentuale) = (pr.Prezzo, Math.Clamp(pr.Sconto, 0, 100));
                prezzi++;
            }
            o.Righe.Add(ro);
        }
        if (o.Righe.Count == 0) throw new OperazioneNonValidaException("La richiesta non ha righe per questo pacchetto.");
        db.Offerte.Add(o);
        if (p.OffertaSceltaId is null) p.OffertaSceltaId = o.Id;
        d.Stato = StatoRisposta.Ricevuta;
        d.RispostaIl = Oggi;
        d.OffertaId = o.Id;
        db.NotaStorico = $"Offerta di {d.Fornitore} registrata dalla {r.Numero}";
        await db.SaveChangesAsync(ct);
        return (o.Id, p.Id, prezzi);
    }

    private static TipoOfferta TipoDa(TipoAffidamento t) => t switch
    {
        TipoAffidamento.FornituraEPosa or TipoAffidamento.Subappalto => TipoOfferta.FornituraEPosa,
        TipoAffidamento.NoloACaldo => TipoOfferta.Nolo,
        TipoAffidamento.LavoriInEconomia => TipoOfferta.SolaPosa,
        _ => TipoOfferta.SolaFornitura,
    };

    // ---- Excel compilato dal fornitore ----

    /// <summary>
    /// Legge l'Excel della richiesta compilato dal fornitore (quello prodotto da Corico): prezzo e sconto di ogni riga, più le
    /// condizioni scritte nelle celle previste. Le righe sono riconosciute dalla colonna nascosta con l'identificativo.
    /// </summary>
    public static PrezziCompilati LeggiExcelCompilato(Stream contenuto)
    {
        using var wb = new XLWorkbook(contenuto);
        var ws = wb.Worksheets.FirstOrDefault(w => w.Name == Esportazioni.EsportazioneExcel.FoglioRichiesta)
            ?? throw new OperazioneNonValidaException("Il file non è l'Excel di una richiesta d'offerta di Corico (manca il foglio «Richiesta»).");
        var righe = new Dictionary<Guid, (decimal, decimal)>();
        string? Condizione(string etichetta) =>
            ws.Column(2).CellsUsed().FirstOrDefault(c => c.GetString().Trim().Equals(etichetta, StringComparison.OrdinalIgnoreCase))
                ?.CellRight().GetString() is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        foreach (var riga in ws.RowsUsed())
        {
            if (!Guid.TryParse(riga.Cell(1).GetString(), out var id)) continue;
            var prezzo = Numero(riga.Cell(Esportazioni.EsportazioneExcel.ColonnaPrezzoRichiesta));
            if (prezzo is null) continue;
            var sconto = Numero(riga.Cell(Esportazioni.EsportazioneExcel.ColonnaPrezzoRichiesta + 1)) ?? 0;
            righe[id] = (prezzo.Value, sconto);
        }
        return new PrezziCompilati(righe, Condizione("Validità dell'offerta (giorni)"), Condizione("Pagamento"), Condizione("Tempi di consegna"),
            Condizione("Esclusioni e oneri a vostro carico"), Condizione("Vostro numero d'offerta"));
    }

    private static decimal? Numero(IXLCell c)
    {
        if (c.IsEmpty()) return null;
        if (c.DataType == XLDataType.Number) return (decimal)c.GetDouble();
        var t = c.GetString().Replace("€", "").Replace(" ", "").Trim();
        if (t.Length == 0) return null;
        if (t.Contains(',')) t = t.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ---- Avvisi ----

    /// <summary>Richieste con fornitori senza risposta oltre la scadenza, per commessa.</summary>
    public async Task<IReadOnlyList<(Guid CommessaId, string Numero, int SenzaRisposta, DateOnly Scadenza)>> ScaduteAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var oggi = Oggi;
        var richieste = await db.Richieste.AsNoTracking().Include(r => r.Destinatari).Where(r => r.Scadenza != null && r.Scadenza < oggi).ToListAsync(ct);
        return richieste.Select(r => (r.CommessaId, r.Numero, r.Destinatari.Count(d => d.Stato is StatoRisposta.DaInviare or StatoRisposta.Inviata), r.Scadenza!.Value))
            .Where(x => x.Item3 > 0).ToList();
    }

    internal static string DescrizioneEstesa(VoceComputo v) =>
        v.VoceElencoPrezzi?.DescrizioneEstesa is { Length: > 0 } e ? e.Trim() : v.Descrizione.Trim();

    private static void Sincronizza<T>(PreventivoDbContext db, List<T> esistenti, IEnumerable<T> nuovi, Action<T, T> copia, Guid commessaId,
        Action<T> collega) where T : EntitaBase, new()
    {
        var elenco = nuovi.ToList();
        var idNuovi = elenco.Where(n => n.Id != Guid.Empty).Select(n => n.Id).ToHashSet();
        foreach (var e in esistenti.Where(e => !idNuovi.Contains(e.Id)).ToList())
        {
            esistenti.Remove(e);
            db.Remove(e);
        }
        foreach (var n in elenco)
        {
            var e = n.Id == Guid.Empty ? null : esistenti.FirstOrDefault(x => x.Id == n.Id);
            if (e is null)
            {
                e = new T();
                typeof(T).GetProperty("CommessaId")!.SetValue(e, commessaId);
                collega(e);
                esistenti.Add(e);
                db.Add(e);
            }
            copia(e, n);
        }
    }

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
