using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record RigaTabellone(
    Guid Id, int Versione, int Ordine, string? Codice, string Descrizione, TipoAffidamento Tipo, string? RiferimentoOda,
    string? Fornitore, StatoCosto Stato, int NumeroVoci, int NumeroOfferte, bool OffertaScelta,
    decimal RicavoNetto, decimal? Costo, NotaPacchetto? UltimaNota)
{
    public decimal? Mdc => Costo is null ? null : RicavoNetto - Costo;
    public decimal? MarginePercentuale => Importi.MargineSulRicavo(RicavoNetto, Costo);
}

public sealed record Tabellone(IReadOnlyList<RigaTabellone> Righe, decimal RibassoPercentuale,
    int VociNonAssegnate, decimal RicavoNonAssegnato)
{
    public decimal RicavoPacchetti => Righe.Sum(r => r.RicavoNetto);
    public decimal CostoPacchetti => Righe.Sum(r => r.Costo ?? 0);
    public decimal RicavoConCosto => Righe.Where(r => r.Costo is not null).Sum(r => r.RicavoNetto);
    public decimal MdcPacchetti => RicavoConCosto - CostoPacchetti;
    public decimal? MarginePercentuale => Importi.MargineSulRicavo(RicavoConCosto, CostoPacchetti);
    public int PacchettiSenzaCosto => Righe.Count(r => r.Costo is null);
}

public sealed record OffertaCalcolata(Offerta Offerta, RisultatoOfferta Risultato, bool Scelta);

public sealed record DettaglioPacchetto(Pacchetto Pacchetto, IReadOnlyList<VoceDiCalcolo> Voci,
    IReadOnlyList<OffertaCalcolata> Offerte, IReadOnlyList<NotaPacchetto> Note, decimal RibassoPercentuale)
{
    public OffertaCalcolata? Scelta => Offerte.FirstOrDefault(o => o.Scelta);
}

/// <summary>Pacchetti di affidamento, offerte alternative, scelta, stato del costo e note (sez. 14.1).</summary>
public sealed class PacchettiService(IDbContextFactory<PreventivoDbContext> fabbrica)
{
    public async Task<Tabellone> TabelloneAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var pacchetti = await db.Pacchetti.AsNoTracking().Include(p => p.Voci)
            .Where(p => p.CommessaId == commessaId && !p.Archiviato).OrderBy(p => p.Ordine).ToListAsync(ct);
        var offerte = await CaricaOfferteAsync(db, commessaId, ct);
        var voci = await VociDiCalcoloAsync(db, commessaId, ct);
        var note = (await db.NotePacchetti.AsNoTracking().Where(n => n.CommessaId == commessaId).ToListAsync(ct))
            .GroupBy(n => n.PacchettoId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.Data).ThenByDescending(n => n.CreatoIl).First());

        var righe = new List<RigaTabellone>();
        foreach (var p in pacchetti)
        {
            var vociPacchetto = VociDel(p, voci);
            var offertePacchetto = offerte.Where(o => o.PacchettoId == p.Id).ToList();
            var scelta = offertePacchetto.FirstOrDefault(o => o.Id == p.OffertaSceltaId);
            decimal ricavo;
            decimal? costo = null;
            if (scelta is not null)
            {
                var r = CalcoloPacchetti.Calcola(scelta, vociPacchetto, preventivo.RibassoPercentuale);
                (ricavo, costo) = (r.RicavoNetto, r.Costo);
            }
            else
            {
                ricavo = CalcoloPacchetti.RicavoDaComputo(vociPacchetto, preventivo.RibassoPercentuale).Netto;
            }
            righe.Add(new RigaTabellone(p.Id, p.Versione, p.Ordine, p.Codice, p.Descrizione, p.Tipo, p.RiferimentoOda,
                scelta?.NomeFornitore, p.StatoCosto, vociPacchetto.Count, offertePacchetto.Count, scelta is not null,
                ricavo, costo, note.GetValueOrDefault(p.Id)));
        }

        var assegnate = pacchetti.SelectMany(p => p.Voci).Select(v => v.VoceComputoId).ToHashSet();
        var computoAttivo = await db.Computi.AsNoTracking().Where(c => c.CommessaId == commessaId && c.Attivo)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var libere = await db.VociComputo.AsNoTracking()
            .Where(v => v.ComputoId == computoAttivo && !v.Archiviata)
            .Select(v => new VoceDiCalcolo(v.Id, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo, v.PercentualeNonSoggetta))
            .ToListAsync(ct);
        libere = libere.Where(v => !assegnate.Contains(v.Id)).ToList();

        return new Tabellone(righe, preventivo.RibassoPercentuale, libere.Count,
            CalcoloPacchetti.RicavoDaComputo(libere, preventivo.RibassoPercentuale).Netto);
    }

    public async Task<DettaglioPacchetto?> DettaglioAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.AsNoTracking().Include(x => x.Voci)
            .FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct);
        if (p is null) return null;
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var voci = VociDel(p, await VociDiCalcoloAsync(db, commessaId, ct));
        // L'offerta di riferimento per prima, poi le alternative nell'ordine di inserimento.
        var offerte = (await CaricaOfferteAsync(db, commessaId, ct)).Where(o => o.PacchettoId == p.Id)
            .OrderBy(o => o.Id == p.OffertaSceltaId ? 0 : 1).ThenBy(o => o.CreatoIl).ToList();
        var note = await db.NotePacchetti.AsNoTracking().Where(n => n.PacchettoId == p.Id)
            .OrderByDescending(n => n.Data).ThenByDescending(n => n.CreatoIl).ToListAsync(ct);
        return new DettaglioPacchetto(p, voci,
            offerte.Select(o => new OffertaCalcolata(o, CalcoloPacchetti.Calcola(o, voci, preventivo.RibassoPercentuale), o.Id == p.OffertaSceltaId)).ToList(),
            note, preventivo.RibassoPercentuale);
    }

    /// <summary>Voci del computo attivo non ancora assegnate a un pacchetto.</summary>
    public async Task<IReadOnlyList<VoceDiCalcolo>> VociLibereAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var assegnate = await db.PacchettiVoci.Where(v => v.CommessaId == commessaId).Select(v => v.VoceComputoId).ToListAsync(ct);
        var computo = await db.Computi.AsNoTracking().FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);
        if (computo is null) return [];
        var voci = await db.VociComputo.AsNoTracking().Where(v => v.ComputoId == computo.Id && !v.Archiviata)
            .OrderBy(v => v.Ordine)
            .Select(v => new VoceDiCalcolo(v.Id, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo, v.PercentualeNonSoggetta))
            .ToListAsync(ct);
        var set = assegnate.ToHashSet();
        return voci.Where(v => !set.Contains(v.Id)).ToList();
    }

    public async Task<Guid> SalvaPacchettoAsync(Guid commessaId, Pacchetto dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Descrizione))
            throw new OperazioneNonValidaException("La descrizione del pacchetto è obbligatoria.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        Pacchetto p;
        if (dati.Id == Guid.Empty || !await db.Pacchetti.AnyAsync(x => x.Id == dati.Id, ct))
        {
            var ultimo = await db.Pacchetti.Where(x => x.CommessaId == commessaId).MaxAsync(x => (int?)x.Ordine, ct) ?? 0;
            p = new Pacchetto { CommessaId = commessaId, Ordine = ultimo + 1 };
            if (dati.Id != Guid.Empty) p.Id = dati.Id;
            db.Pacchetti.Add(p);
        }
        else
        {
            p = await db.Pacchetti.FirstAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct);
            db.Entry(p).Property(x => x.Versione).OriginalValue = dati.Versione;
        }
        p.Codice = Pulisci(dati.Codice);
        p.Descrizione = dati.Descrizione.Trim();
        p.Tipo = dati.Tipo;
        p.RiferimentoOda = Pulisci(dati.RiferimentoOda);
        p.StatoCosto = dati.StatoCosto;
        p.Note = Pulisci(dati.Note);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task ImpostaArchiviazioneAsync(Guid commessaId, Guid pacchettoId, bool archiviato, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.Include(x => x.Voci).FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il pacchetto non esiste.");
        p.Archiviato = archiviato;
        if (archiviato)
        {
            // Le voci tornano disponibili per altri pacchetti; le offerte restano consultabili.
            db.PacchettiVoci.RemoveRange(p.Voci);
            db.NotaStorico = $"Pacchetto archiviato: {p.Voci.Count} voci tornano non assegnate";
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Assegna voci al pacchetto. Una voce già in un altro pacchetto viene spostata; restituisce quante sono state spostate.</summary>
    public async Task<int> AssegnaVociAsync(Guid commessaId, Guid pacchettoId, IReadOnlyCollection<Guid> voci, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        if (!await db.Pacchetti.AnyAsync(p => p.Id == pacchettoId && p.CommessaId == commessaId, ct))
            throw new OperazioneNonValidaException("Il pacchetto non esiste.");
        var esistenti = await db.PacchettiVoci.Where(v => v.CommessaId == commessaId && voci.Contains(v.VoceComputoId)).ToListAsync(ct);
        var spostate = esistenti.Where(e => e.PacchettoId != pacchettoId).ToList();
        foreach (var e in spostate) await StaccaDalleOfferteAsync(db, e.PacchettoId, e.VoceComputoId, ct);
        db.PacchettiVoci.RemoveRange(spostate);
        var gia = esistenti.Where(e => e.PacchettoId == pacchettoId).Select(e => e.VoceComputoId).ToHashSet();
        foreach (var id in voci.Where(v => !gia.Contains(v)))
            db.PacchettiVoci.Add(new PacchettoVoce { PacchettoId = pacchettoId, CommessaId = commessaId, VoceComputoId = id });
        if (spostate.Count > 0) db.NotaStorico = $"{spostate.Count} voci spostate da un altro pacchetto";
        await db.SaveChangesAsync(ct);
        return spostate.Count;
    }

    public async Task RimuoviVoceAsync(Guid commessaId, Guid pacchettoId, Guid voceId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var pv = await db.PacchettiVoci.FirstOrDefaultAsync(v => v.PacchettoId == pacchettoId && v.VoceComputoId == voceId && v.CommessaId == commessaId, ct);
        if (pv is null) return;
        await StaccaDalleOfferteAsync(db, pacchettoId, voceId, ct);
        db.PacchettiVoci.Remove(pv);
        await db.SaveChangesAsync(ct);
    }

    public async Task AggiungiNotaAsync(Guid commessaId, Guid pacchettoId, DateOnly data, string? iniziali, string testo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(testo)) throw new OperazioneNonValidaException("Il testo della nota è obbligatorio.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        db.NotePacchetti.Add(new NotaPacchetto
        {
            PacchettoId = pacchettoId, CommessaId = commessaId, Data = data,
            Iniziali = Pulisci(iniziali)?.ToUpperInvariant(), Testo = testo.Trim(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task ScegliOffertaAsync(Guid commessaId, Guid pacchettoId, int versioneLetta, Guid? offertaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il pacchetto non esiste.");
        if (offertaId is not null && !await db.Offerte.AnyAsync(o => o.Id == offertaId && o.PacchettoId == pacchettoId, ct))
            throw new OperazioneNonValidaException("L'offerta non appartiene al pacchetto.");
        db.Entry(p).Property(x => x.Versione).OriginalValue = versioneLetta;
        p.OffertaSceltaId = offertaId;
        db.NotaStorico = offertaId is null ? "Nessuna offerta di riferimento" : "Scelta dell'offerta di riferimento";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Offerta vuota pronta per l'inserimento, con una riga di costo per ogni voce del pacchetto.</summary>
    public async Task<Offerta> NuovaOffertaAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var voci = await db.PacchettiVoci.Where(v => v.PacchettoId == pacchettoId).Select(v => v.VoceComputoId).ToListAsync(ct);
        var o = new Offerta { Id = Guid.Empty, PacchettoId = pacchettoId, CommessaId = commessaId, Tipo = TipoOfferta.SolaFornitura };
        foreach (var v in voci) o.Voci.Add(new OffertaVoce { Id = Guid.Empty, CommessaId = commessaId, VoceComputoId = v });
        return o;
    }

    public async Task<Offerta?> OffertaAsync(Guid commessaId, Guid offertaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Offerte.AsNoTracking().AsSplitQuery().Include(x => x.Voci).Include(x => x.Righe).Include(x => x.CostiACorpo)
            .FirstOrDefaultAsync(x => x.Id == offertaId && x.CommessaId == commessaId, ct);
        if (o is null) return null;
        // Le voci aggiunte al pacchetto dopo l'inserimento dell'offerta compaiono con costo zero.
        var voci = await db.PacchettiVoci.Where(v => v.PacchettoId == o.PacchettoId).Select(v => v.VoceComputoId).ToListAsync(ct);
        foreach (var v in voci.Where(v => o.Voci.All(x => x.VoceComputoId != v)))
            o.Voci.Add(new OffertaVoce { Id = Guid.Empty, OffertaId = o.Id, CommessaId = commessaId, VoceComputoId = v });
        o.Righe.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        o.CostiACorpo.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        return o;
    }

    /// <summary>Salva testata, costi per voce, righe e voci a corpo di un'offerta (inserimento o modifica).</summary>
    public async Task<Guid> SalvaOffertaAsync(Guid commessaId, Offerta dati, CancellationToken ct = default)
    {
        Valida(dati);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var vociPacchetto = await db.PacchettiVoci.Where(v => v.PacchettoId == dati.PacchettoId && v.CommessaId == commessaId)
            .Select(v => v.VoceComputoId).ToListAsync(ct);
        var quantita = await db.VociComputo.Where(v => vociPacchetto.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Quantita, ct);
        foreach (var v in dati.Voci.Where(v => v.QuantitaPianificazione is { } q && quantita.TryGetValue(v.VoceComputoId, out var qc) && q != qc))
        {
            if (string.IsNullOrWhiteSpace(v.NotaQuantita))
                throw new OperazioneNonValidaException("Quando la quantità di pianificazione è diversa da quella del computo, la nota è obbligatoria.");
        }

        Offerta o;
        if (dati.Id == Guid.Empty)
        {
            if (!await db.Pacchetti.AnyAsync(p => p.Id == dati.PacchettoId && p.CommessaId == commessaId, ct))
                throw new OperazioneNonValidaException("Il pacchetto non esiste.");
            o = new Offerta { PacchettoId = dati.PacchettoId, CommessaId = commessaId };
            db.Offerte.Add(o);
        }
        else
        {
            o = await db.Offerte.AsSplitQuery().Include(x => x.Voci).Include(x => x.Righe).Include(x => x.CostiACorpo)
                .FirstOrDefaultAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct)
                ?? throw new OperazioneNonValidaException("L'offerta non esiste.");
            db.Entry(o).Property(x => x.Versione).OriginalValue = dati.Versione;
        }

        o.FornitoreId = dati.FornitoreId;
        o.NomeFornitore = Pulisci(dati.NomeFornitore);
        o.Tipo = dati.Tipo;
        o.Numero = Pulisci(dati.Numero);
        o.Revisione = Pulisci(dati.Revisione);
        o.Data = dati.Data;
        o.ValiditaGiorni = dati.ValiditaGiorni;
        o.Scadenza = dati.Scadenza ?? (dati.Data is { } d && dati.ValiditaGiorni is { } g ? d.AddDays(g) : null);
        o.Pagamento = Pulisci(dati.Pagamento);
        o.Consegna = Pulisci(dati.Consegna);
        o.Garanzia = Pulisci(dati.Garanzia);
        o.DocumentoId = dati.DocumentoId;
        o.Esclusioni = Pulisci(dati.Esclusioni);
        o.TotaleDichiarato = dati.TotaleDichiarato;
        o.Note = Pulisci(dati.Note);
        o.Origine = Pulisci(dati.Origine);

        Sincronizza(db, o.Voci, dati.Voci.Where(v => vociPacchetto.Contains(v.VoceComputoId)), (a, b) =>
        {
            a.VoceComputoId = b.VoceComputoId;
            a.QuantitaPianificazione = b.QuantitaPianificazione;
            a.NotaQuantita = Pulisci(b.NotaQuantita);
            a.Fornitura = b.Fornitura;
            a.FornituraEPosa = b.FornituraEPosa;
            a.ManodoperaENoli = b.ManodoperaENoli;
        }, commessaId);
        var ordine = 0;
        Sincronizza(db, o.Righe, dati.Righe, (a, b) =>
        {
            a.Ordine = ++ordine;
            a.CodiceArticolo = Pulisci(b.CodiceArticolo);
            a.Descrizione = b.Descrizione.Trim();
            a.Posizione = Pulisci(b.Posizione);
            a.Unita = Importazione.UnitaMisura.Normalizza(b.Unita);
            a.Quantita = b.Quantita;
            a.PrezzoListino = b.PrezzoListino;
            a.ScontoPercentuale = b.ScontoPercentuale;
            a.VoceComputoId = b.VoceComputoId is { } v && vociPacchetto.Contains(v) ? v : null;
        }, commessaId);
        ordine = 0;
        Sincronizza(db, o.CostiACorpo, dati.CostiACorpo, (a, b) =>
        {
            a.Ordine = ++ordine;
            a.Descrizione = b.Descrizione.Trim();
            a.Incluso = b.Incluso;
            a.Importo = b.Incluso ? 0 : b.Importo;
        }, commessaId);

        await db.SaveChangesAsync(ct);

        // La prima offerta inserita diventa quella di riferimento, finché l'utente non ne sceglie un'altra.
        var p = await db.Pacchetti.FirstAsync(x => x.Id == o.PacchettoId, ct);
        if (p.OffertaSceltaId is null)
        {
            p.OffertaSceltaId = o.Id;
            await db.SaveChangesAsync(ct);
        }
        return o.Id;
    }

    public async Task EliminaOffertaAsync(Guid commessaId, Guid offertaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Offerte.FirstOrDefaultAsync(x => x.Id == offertaId && x.CommessaId == commessaId, ct);
        if (o is null) return;
        var p = await db.Pacchetti.FirstAsync(x => x.Id == o.PacchettoId, ct);
        if (p.OffertaSceltaId == o.Id) p.OffertaSceltaId = null;
        db.Offerte.Remove(o);
        db.NotaStorico = $"Offerta eliminata: {o.NomeFornitore} {o.Numero}";
        await db.SaveChangesAsync(ct);
    }

    private static void Valida(Offerta o)
    {
        if (o.Righe.Any(r => string.IsNullOrWhiteSpace(r.Descrizione)))
            throw new OperazioneNonValidaException("Ogni riga dell'offerta deve avere una descrizione.");
        if (o.Righe.Any(r => r.ScontoPercentuale is < 0 or > 100))
            throw new OperazioneNonValidaException("Lo sconto deve essere compreso tra 0 e 100%.");
        if (o.CostiACorpo.Any(c => string.IsNullOrWhiteSpace(c.Descrizione)))
            throw new OperazioneNonValidaException("Ogni voce a corpo deve avere una descrizione.");
        if (o.ValiditaGiorni is < 0)
            throw new OperazioneNonValidaException("La validità non può essere negativa.");
    }

    private static void Sincronizza<T>(PreventivoDbContext db, List<T> esistenti, IEnumerable<T> nuovi, Action<T, T> copia, Guid commessaId)
        where T : EntitaBase, new()
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
                esistenti.Add(e);
                // Aggiunta esplicita: con la chiave già valorizzata EF la considererebbe una riga esistente da aggiornare.
                db.Add(e);
            }
            copia(e, n);
        }
    }

    private static async Task StaccaDalleOfferteAsync(PreventivoDbContext db, Guid pacchettoId, Guid voceId, CancellationToken ct)
    {
        var offerte = await db.Offerte.Where(o => o.PacchettoId == pacchettoId).Select(o => o.Id).ToListAsync(ct);
        db.OfferteVoci.RemoveRange(await db.OfferteVoci.Where(v => offerte.Contains(v.OffertaId) && v.VoceComputoId == voceId).ToListAsync(ct));
        foreach (var r in await db.RigheOfferta.Where(r => offerte.Contains(r.OffertaId) && r.VoceComputoId == voceId).ToListAsync(ct))
            r.VoceComputoId = null;
    }

    private static async Task<List<Offerta>> CaricaOfferteAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct) =>
        await db.Offerte.AsNoTracking().AsSplitQuery().Include(o => o.Voci).Include(o => o.Righe).Include(o => o.CostiACorpo)
            .Where(o => o.CommessaId == commessaId).ToListAsync(ct);

    private static async Task<Dictionary<Guid, (VoceDiCalcolo Voce, int Ordine, bool Archiviata)>> VociDiCalcoloAsync(
        PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var ids = await db.PacchettiVoci.Where(v => v.CommessaId == commessaId).Select(v => v.VoceComputoId).ToListAsync(ct);
        var voci = await db.VociComputo.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        return voci.ToDictionary(v => v.Id, v => (new VoceDiCalcolo(v.Id, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo,
            v.PercentualeNonSoggetta), v.Ordine, v.Archiviata));
    }

    private static List<VoceDiCalcolo> VociDel(Pacchetto p, Dictionary<Guid, (VoceDiCalcolo Voce, int Ordine, bool Archiviata)> voci) =>
        p.Voci.Select(v => voci.GetValueOrDefault(v.VoceComputoId))
            .Where(v => v.Voce is not null && !v.Archiviata)
            .OrderBy(v => v.Ordine).Select(v => v.Voce).ToList();

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
