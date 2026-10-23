using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Controllo di una riga della bozza rispetto alla Lista della Spesa attuale.</summary>
public sealed record InfoRigaBozza(Guid RigaId, decimal? DaOrdinare, string? Pacchetti, bool Eccedenza, bool PrezzoDiverso);

/// <summary>Esito dei controlli di una bozza: gli impedimenti bloccano l'emissione, gli avvisi no.</summary>
public sealed record VerificaBozza(
    IReadOnlyList<InfoRigaBozza> Righe, IReadOnlyList<string> Impedimenti, IReadOnlyList<string> Avvisi,
    bool AziendaCompilata, bool PuoEmettere)
{
    public bool Emettibile => Impedimenti.Count == 0;
    public InfoRigaBozza? Di(Guid rigaId) => Righe.FirstOrDefault(r => r.RigaId == rigaId);
}

public sealed record RigaElencoOrdini(
    Guid Id, string Numero, DateOnly Data, string Fornitore, string Oggetto, Guid? PacchettoId, string Pacchetto,
    decimal Totale, StatoOrdine Stato, string? MotivoAnnullamento, DateOnly? AnnullatoIl,
    string? PreparatoDa = null, string? EmessoDa = null, DateOnly? InviatoIl = null, DateOnly? ConfermatoIl = null);

public sealed record TotaleAcquisti(string Nome, decimal Pianificato, decimal Impegnato, decimal Residuo);

/// <summary>Totali della scheda Ordini: costo pianificato, impegnato con ordini emessi e residuo da ordinare.</summary>
public sealed record RiepilogoAcquisti(decimal Pianificato, decimal Impegnato, decimal Residuo,
    IReadOnlyList<TotaleAcquisti> PerPacchetto, IReadOnlyList<TotaleAcquisti> PerFornitore);

/// <summary>
/// Ordini di acquisto (Modulo 2, rilascio 2A): si preparano in bozza dal pacchetto o dalla Lista della Spesa, anche parziali
/// e con righe di più pacchetti dello stesso fornitore; all'emissione ricevono il numero progressivo dell'anno e diventano
/// immutabili. Emettono l'amministratore e gli operatori abilitati. Il pacchetto diventa Definitivo quando tutte le sue righe
/// sono ordinate (decisione 2); il costo attuale è l'importo degli ordini (decisione 1).
/// </summary>
public sealed class OrdiniService(
    IDbContextFactory<PreventivoDbContext> fabbrica, CommesseService commesse, FornitoriService fornitori,
    ImpostazioniService impostazioni, IUtenteCorrente utente, TimeProvider orologio)
{
    private static readonly SemaphoreSlim BloccoNumeri = new(1, 1);

    /// <summary>Tolleranza sulle quantità (arrotondamenti dei fabbisogni).</summary>
    private const decimal Tolleranza = 0.0001m;

    public async Task<IReadOnlyList<RigaElencoOrdini>> ElencoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var pacchetti = await db.Pacchetti.AsNoTracking().Where(p => p.CommessaId == commessaId)
            .ToDictionaryAsync(p => p.Id, p => p.Descrizione, ct);
        var ordini = await db.Ordini.AsNoTracking().Include(o => o.Righe).ThenInclude(r => r.Origini)
            .Where(o => o.CommessaId == commessaId).ToListAsync(ct);
        return ordini.OrderBy(o => o.Stato == StatoOrdine.Bozza ? 0 : 1).ThenByDescending(o => o.Anno).ThenByDescending(o => o.Progressivo)
            .ThenByDescending(o => o.CreatoIl)
            .Select(o => new RigaElencoOrdini(o.Id, o.NumeroVisibile, o.Data, o.Fornitore, o.Oggetto, o.PacchettoId,
                string.Join(", ", PacchettiDi(o).Select(id => pacchetti.GetValueOrDefault(id, "")).Where(n => n.Length > 0).Distinct()),
                o.Totale, o.Stato, o.MotivoAnnullamento, o.AnnullatoIl, o.CreatoDa, o.EmessoDa, o.InviatoIl, o.ConfermatoIl))
            .ToList();
    }

    /// <summary>Ordini che riguardano il pacchetto: preparati dal pacchetto o con righe delle sue voci.</summary>
    public async Task<IReadOnlyList<OrdineAcquisto>> PerPacchettoAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var ordini = await db.Ordini.AsNoTracking().Include(o => o.Righe).ThenInclude(r => r.Origini)
            .Where(o => o.CommessaId == commessaId).ToListAsync(ct);
        return ordini.Where(o => PacchettiDi(o).Contains(pacchettoId))
            .OrderBy(o => o.Stato == StatoOrdine.Bozza ? 0 : 1).ThenByDescending(o => o.Anno).ThenByDescending(o => o.Progressivo).ToList();
    }

    public async Task<OrdineAcquisto?> OttieniAsync(Guid commessaId, Guid ordineId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.AsNoTracking().Include(x => x.Righe).ThenInclude(r => r.Origini)
            .FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct);
        o?.Righe.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        return o;
    }

    /// <summary>Pacchetti toccati dall'ordine: quello di partenza, quelli delle righe e delle loro origini.</summary>
    public static IEnumerable<Guid> PacchettiDi(OrdineAcquisto o) =>
        new[] { o.PacchettoId }.Concat(o.Righe.Select(r => r.PacchettoId)).Where(x => x is not null).Select(x => x!.Value)
            .Concat(o.Righe.SelectMany(r => r.Origini).Select(x => x.PacchettoId)).Distinct();

    // ---- Preparazione della bozza ----

    /// <summary>Bozza con tutto ciò che resta da ordinare del pacchetto, dal fornitore dell'offerta scelta.</summary>
    public async Task<Guid> PreparaDaPacchettoAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il pacchetto non esiste.");
        if (p.OffertaSceltaId is null)
            throw new OperazioneNonValidaException("Il pacchetto non ha un'offerta scelta: scegli un'offerta (o una stima interna) prima di preparare l'ordine.");
        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        var righe = lista.Righe.Where(r => r.PacchettoId == pacchettoId).ToList();
        if (righe.Count == 0)
            throw new OperazioneNonValidaException("L'offerta scelta non ha importi da ordinare.");
        if (righe.All(r => r.DaOrdinare <= Tolleranza && r.Ordinata))
            throw new OperazioneNonValidaException("Il pacchetto è già interamente ordinato.");
        return await CreaBozzaAsync(db, commessaId, righe.Where(r => !r.Ordinata).ToList(), p, ct);
    }

    /// <summary>
    /// Bozza dalle righe selezionate della Lista della Spesa (per chiave): quantità proposta = quantità ancora da ordinare,
    /// prezzo = prezzo pianificato. La stessa risorsa di più voci diventa un'unica riga d'ordine.
    /// </summary>
    public async Task<Guid> PreparaDaListaAsync(Guid commessaId, IReadOnlyCollection<string> chiavi, CancellationToken ct = default)
    {
        if (chiavi.Count == 0) throw new OperazioneNonValidaException("Seleziona almeno una riga della Lista della Spesa.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        var set = chiavi.ToHashSet();
        var selezionate = lista.Righe.Where(r => set.Contains(r.ChiaveOrdine)).ToList();
        if (selezionate.Count == 0)
            throw new OperazioneNonValidaException("Le righe selezionate non sono più nella Lista della Spesa: aggiorna la pagina.");
        var daOrdinare = selezionate.Where(r => !r.Ordinata).ToList();
        if (daOrdinare.Count == 0)
            throw new OperazioneNonValidaException("Le righe selezionate risultano già interamente ordinate.");
        var pacchetti = daOrdinare.Select(r => r.PacchettoId).Distinct().ToList();
        var unico = pacchetti.Count == 1
            ? await db.Pacchetti.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pacchetti[0], ct)
            : null;
        return await CreaBozzaAsync(db, commessaId, daOrdinare, unico, ct);
    }

    private async Task<Guid> CreaBozzaAsync(PreventivoDbContext db, Guid commessaId, List<RigaSpesa> righe, Pacchetto? pacchetto, CancellationToken ct)
    {
        var nominati = righe.Where(r => !r.FornitoreLibero).Select(r => r.Fornitore!).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (nominati.Count > 1)
            throw new OperazioneNonValidaException(
                $"Un ordine raccoglie righe di un solo fornitore: hai selezionato righe di {string.Join(", ", nominati)}. Filtra la lista per fornitore.");
        var commessa = await commesse.OttieniAsync(commessaId, ct) ?? throw new OperazioneNonValidaException("La commessa non esiste.");

        var offerte = righe.Where(r => r.OffertaId is not null).Select(r => r.OffertaId!.Value).Distinct().ToList();
        var offerta = offerte.Count == 1 ? await db.Offerte.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerte[0], ct) : null;
        var conFornitore = righe.FirstOrDefault(r => !r.FornitoreLibero);
        var fornitoreId = conFornitore?.FornitoreId;
        var nome = conFornitore?.Fornitore ?? "";
        if (fornitoreId is { } fid && await fornitori.OttieniAsync(fid, ct) is { } f) nome = f.Denominazione;

        var ordine = new OrdineAcquisto
        {
            CommessaId = commessaId,
            PacchettoId = pacchetto?.Id,
            OffertaId = offerta?.Id,
            Data = Oggi(),
            Stato = StatoOrdine.Bozza,
            FornitoreId = fornitoreId,
            Fornitore = nome.Trim(),
            FornitoreDaOfferta = conFornitore is not null,
            Oggetto = $"Commessa {commessa.Codice} – {pacchetto?.Descrizione ?? "acquisti dalla Lista della Spesa"}",
            LuogoConsegna = Cantiere(commessa),
            RiferimentoOfferta = conFornitore is not null && offerta is not null ? RiferimentoOfferta(offerta) : null,
            Consegna = offerta?.Consegna,
            Pagamento = offerta?.Pagamento,
        };

        foreach (var g in righe.GroupBy(r => (r.Categoria, r.Chiave, r.Prezzo)).OrderBy(g => g.Key.Categoria).ThenBy(g => g.First().Codice))
        {
            var primo = g.First();
            var quantita = g.Sum(r => r.DaOrdinare);
            var prezzo = primo.Prezzo ?? (primo.Quantita == 0 ? primo.Importo : primo.Importo / primo.Quantita);
            if (quantita == 0 && primo.Quantita == 0) quantita = 1;
            var riga = new RigaOrdine
            {
                OrdineId = ordine.Id, CommessaId = commessaId, Ordine = ordine.Righe.Count + 1,
                Codice = primo.Codice, Descrizione = primo.Descrizione.Trim(), Unita = primo.Unita, Categoria = primo.Categoria,
                Quantita = Importi.Arrotonda(quantita, 4), Prezzo = Importi.Arrotonda(prezzo, 4), PrezzoPianificato = Importi.Arrotonda(prezzo, 4),
            };
            riga.Importo = Importi.Arrotonda(riga.Quantita * riga.Prezzo);
            foreach (var r in g)
                riga.Origini.Add(new RigaOrdineOrigine
                {
                    RigaOrdineId = riga.Id, OrdineId = ordine.Id, CommessaId = commessaId, Chiave = r.ChiaveOrdine,
                    PacchettoId = r.PacchettoId, VoceComputoId = r.VoceId, Quantita = r.DaOrdinare,
                });
            ordine.Righe.Add(riga);
        }
        ordine.Totale = ordine.Righe.Sum(r => r.Importo);
        db.Ordini.Add(ordine);
        db.InserimentiSoloInRiepilogo = true;
        db.RegistraOperazione("Preparazione ordine", nameof(OrdineAcquisto), ordine.Id, commessaId,
            $"Bozza d'ordine{(ordine.Fornitore.Length > 0 ? " a " + ordine.Fornitore : "")}: {ordine.Righe.Count} righe, {Formato.Euro(ordine.Totale)}");
        await db.SaveChangesAsync(ct);
        return ordine.Id;
    }

    // ---- Modifica della bozza ----

    /// <summary>
    /// Salva testata e righe della bozza: quantità (anche ridotte, per consegne frazionate), prezzi con nota, righe libere
    /// (trasporto, imballo) imputate a un pacchetto. Le quantità si ripartiscono sulle voci di origine fino a quanto
    /// resta da ordinare; l'eventuale eccedenza va sull'ultima.
    /// </summary>
    public async Task SalvaBozzaAsync(Guid commessaId, OrdineAcquisto dati, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.Include(x => x.Righe).ThenInclude(r => r.Origini)
            .FirstOrDefaultAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'ordine non esiste più.");
        if (o.Stato != StatoOrdine.Bozza) throw new OperazioneNonValidaException("L'ordine è già emesso: non si modifica più.");
        db.Entry(o).Property(x => x.Versione).OriginalValue = dati.Versione;

        o.Data = dati.Data;
        o.Oggetto = (dati.Oggetto ?? "").Trim();
        o.LuogoConsegna = Pulisci(dati.LuogoConsegna);
        o.Consegna = Pulisci(dati.Consegna);
        o.Pagamento = Pulisci(dati.Pagamento);
        o.Note = Pulisci(dati.Note);
        if (!o.FornitoreDaOfferta)
        {
            o.FornitoreId = dati.FornitoreId;
            o.Fornitore = (dati.FornitoreId is { } fid ? (await fornitori.OttieniAsync(fid, ct))?.Denominazione : dati.Fornitore)?.Trim() ?? "";
        }

        var predefinito = o.PacchettoId ?? o.Righe.SelectMany(r => r.Origini).Select(x => (Guid?)x.PacchettoId).FirstOrDefault();
        var arrivate = dati.Righe.ToDictionary(r => r.Id);
        foreach (var r in o.Righe.ToList())
        {
            if (!arrivate.Remove(r.Id, out var n))
            {
                o.Righe.Remove(r);
                db.RigheOrdine.Remove(r);
                continue;
            }
            r.Ordine = n.Ordine;
            r.Descrizione = (n.Descrizione ?? "").Trim();
            r.Quantita = n.Quantita;
            r.Prezzo = n.Prezzo;
            r.NotaScostamento = Pulisci(n.NotaScostamento);
            if (r.Origini.Count == 0)
            {
                r.Codice = Pulisci(n.Codice);
                r.Unita = Pulisci(n.Unita);
                r.PacchettoId = n.PacchettoId ?? predefinito;
            }
        }
        foreach (var n in arrivate.Values)
        {
            var r = new RigaOrdine
            {
                OrdineId = o.Id, CommessaId = commessaId, Ordine = n.Ordine, Codice = Pulisci(n.Codice),
                Descrizione = (n.Descrizione ?? "").Trim(), Unita = Pulisci(n.Unita), Quantita = n.Quantita, Prezzo = n.Prezzo,
                NotaScostamento = Pulisci(n.NotaScostamento), PacchettoId = n.PacchettoId ?? predefinito,
            };
            o.Righe.Add(r);
            db.RigheOrdine.Add(r);
        }
        var numero = 1;
        foreach (var r in o.Righe.OrderBy(r => r.Ordine)) r.Ordine = numero++;

        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        Ripartisci(o, Residui(lista));
        foreach (var r in o.Righe) r.Importo = Importi.Arrotonda(r.Quantita * r.Prezzo);
        o.Totale = o.Righe.Sum(r => r.Importo);
        db.NotaStorico = "Bozza d'ordine";
        await db.SaveChangesAsync(ct);
    }

    public async Task EliminaBozzaAsync(Guid commessaId, Guid ordineId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.Include(x => x.Righe).ThenInclude(r => r.Origini)
            .FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct);
        if (o is null) return;
        if (o.Stato != StatoOrdine.Bozza) throw new OperazioneNonValidaException("Un ordine emesso non si elimina: si annulla con un motivo.");
        db.Ordini.Remove(o);
        db.InserimentiSoloInRiepilogo = true;
        db.RegistraOperazione("Eliminazione bozza d'ordine", nameof(OrdineAcquisto), o.Id, commessaId,
            $"Bozza d'ordine{(o.Fornitore.Length > 0 ? " a " + o.Fornitore : "")} eliminata ({Formato.Euro(o.Totale)})");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Controlli della bozza rispetto alla Lista della Spesa attuale (anche dopo ordini emessi da altri).</summary>
    public async Task<VerificaBozza> VerificaAsync(Guid commessaId, Guid ordineId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.AsNoTracking().Include(x => x.Righe).ThenInclude(r => r.Origini)
            .FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'ordine non esiste più.");
        return await VerificaAsync(db, o, ct);
    }

    private async Task<VerificaBozza> VerificaAsync(PreventivoDbContext db, OrdineAcquisto o, CancellationToken ct)
    {
        var lista = await ListaSpesaService.CalcolaAsync(db, o.CommessaId, ct);
        var residui = Residui(lista);
        var chiaviLista = lista.Righe.Select(r => r.ChiaveOrdine).ToHashSet();
        var nomi = await db.Pacchetti.AsNoTracking().Where(p => p.CommessaId == o.CommessaId).ToDictionaryAsync(p => p.Id, p => p.Descrizione, ct);
        var azienda = await impostazioni.LeggiAziendaAsync(ct);

        var impedimenti = new List<string>();
        var avvisi = new List<string>();
        var info = new List<InfoRigaBozza>();
        if (o.Stato != StatoOrdine.Bozza) impedimenti.Add("L'ordine è già emesso.");
        if (string.IsNullOrWhiteSpace(o.Fornitore)) impedimenti.Add("Scegli il fornitore dell'ordine dall'anagrafica.");
        if (string.IsNullOrWhiteSpace(o.Oggetto)) impedimenti.Add("L'oggetto dell'ordine è obbligatorio.");
        if (o.Righe.Count == 0 || o.Righe.All(r => r.Importo == 0)) impedimenti.Add("L'ordine non ha importi da ordinare.");

        foreach (var r in o.Righe.OrderBy(r => r.Ordine))
        {
            var etichetta = $"Riga {r.Ordine}";
            decimal? daOrdinare = r.Origini.Count == 0 ? null : r.Origini.Sum(x => residui.GetValueOrDefault(x.Chiave));
            var eccedenza = daOrdinare is { } d && r.Quantita > d + Tolleranza;
            var prezzoDiverso = r.PrezzoPianificato is { } pp && Importi.Arrotonda(pp, 4) != Importi.Arrotonda(r.Prezzo, 4);
            var pacchetti = string.Join(", ", (r.Origini.Count > 0 ? r.Origini.Select(x => x.PacchettoId) : new[] { r.PacchettoId ?? Guid.Empty })
                .Distinct().Select(id => nomi.GetValueOrDefault(id, "")).Where(n => n.Length > 0));
            info.Add(new InfoRigaBozza(r.Id, daOrdinare, pacchetti, eccedenza, prezzoDiverso));

            if (string.IsNullOrWhiteSpace(r.Descrizione)) impedimenti.Add($"{etichetta}: la descrizione è obbligatoria.");
            if (r.Quantita <= 0) impedimenti.Add($"{etichetta}: la quantità deve essere maggiore di zero (per toglierla, elimina la riga).");
            if (r.Prezzo < 0) impedimenti.Add($"{etichetta}: il prezzo non può essere negativo.");
            if (eccedenza && r.NotaScostamento is null)
                impedimenti.Add($"{etichetta}: quantità oltre il pianificato (da ordinare {Formato.Numero(daOrdinare!.Value)}): indica il motivo nella nota.");
            if (prezzoDiverso && r.NotaScostamento is null)
                impedimenti.Add($"{etichetta}: prezzo diverso dal pianificato ({Formato.Euro(r.PrezzoPianificato)}): indica il motivo nella nota.");
            if (r.Origini.Count == 0 && r.PacchettoId is null)
                impedimenti.Add($"{etichetta}: indica il pacchetto a cui imputare il costo.");
            if (r.Origini.Any(x => !chiaviLista.Contains(x.Chiave)))
                avvisi.Add($"{etichetta}: non corrisponde più alla Lista della Spesa (offerta o voci cambiate dopo la preparazione). Verificala, o prepara di nuovo l'ordine.");
        }

        if (!await db.Versioni.AsNoTracking().AnyAsync(v => v.CommessaId == o.CommessaId && v.StatoBudget == StatoBudget.Approvato, ct))
            avvisi.Add("Il Budget 0 della commessa non è ancora approvato: puoi emettere l'ordine, ma il costo budget di riferimento non è ancora fissato.");
        if (o.FornitoreId is null && o.Fornitore.Length > 0 && !o.FornitoreDaOfferta)
            avvisi.Add("Il fornitore non è nell'anagrafica: l'ordine non riporterà indirizzo e partita IVA.");

        return new VerificaBozza(info, impedimenti, avvisi, azienda.Compilati, UtenteCorrente.PuoEmettereOrdini);
    }

    // ---- Emissione e annullamento ----

    /// <summary>
    /// Emette la bozza: numero progressivo dell'anno, dati del fornitore e condizioni generali dell'impresa.
    /// Riservato all'amministratore e agli operatori abilitati. Aggiorna lo stato del costo dei pacchetti coinvolti.
    /// </summary>
    public async Task<OrdineAcquisto> EmettiAsync(Guid commessaId, Guid ordineId, CancellationToken ct = default)
    {
        if (!UtenteCorrente.PuoEmettereOrdini)
            throw new OperazioneNonValidaException("L'emissione degli ordini è riservata all'amministratore e agli operatori abilitati.");

        await BloccoNumeri.WaitAsync(ct);
        try
        {
            await using var db = await fabbrica.CreateDbContextAsync(ct);
            await using var transazione = await db.Database.BeginTransactionAsync(ct);
            var o = await db.Ordini.Include(x => x.Righe).ThenInclude(r => r.Origini)
                .FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct)
                ?? throw new OperazioneNonValidaException("L'ordine non esiste più.");
            var verifica = await VerificaAsync(db, o, ct);
            if (!verifica.Emettibile) throw new OperazioneNonValidaException(verifica.Impedimenti[0]);

            var azienda = await impostazioni.LeggiAziendaAsync(ct);
            var anno = o.Data.Year;
            var progressivo = (await db.Ordini.Where(x => x.Anno == anno && x.Progressivo > 0).MaxAsync(x => (int?)x.Progressivo, ct) ?? 0) + 1;
            o.Anno = anno;
            o.Progressivo = progressivo;
            o.Numero = OrdineAcquisto.FormattaNumero(anno, progressivo);
            o.Stato = StatoOrdine.Emesso;
            o.EmessoDa = utente.Nome;
            o.Condizioni = Pulisci(azienda.CondizioniOrdine);
            if (o.FornitoreId is { } fid && await fornitori.OttieniAsync(fid, ct) is { } f)
            {
                o.Fornitore = f.Denominazione.Trim();
                o.DatiFornitore = DatiDel(f);
            }

            db.NotaStorico = $"Emissione dell'ordine {o.Numero}";
            db.RegistraOperazione("Emissione ordine", nameof(OrdineAcquisto), o.Id, commessaId,
                $"{o.Numero} a {o.Fornitore}: {Formato.Euro(o.Totale)}",
                dopo: new { o.Numero, o.Fornitore, o.Totale, Righe = o.Righe.Count });
            await db.SaveChangesAsync(ct);
            await AggiornaPacchettiAsync(db, commessaId, PacchettiDi(o).ToList(), ct);
            await transazione.CommitAsync(ct);
            return o;
        }
        finally
        {
            BloccoNumeri.Release();
        }
    }

    /// <summary>Annulla un ordine emesso, con un motivo: le sue quantità tornano «da ordinare».</summary>
    public async Task AnnullaAsync(Guid commessaId, Guid ordineId, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new OperazioneNonValidaException("Il motivo dell'annullamento è obbligatorio.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        await using var transazione = await db.Database.BeginTransactionAsync(ct);
        var o = await db.Ordini.Include(x => x.Righe).ThenInclude(r => r.Origini)
            .FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'ordine non esiste.");
        if (o.Stato == StatoOrdine.Annullato)
            throw new OperazioneNonValidaException("L'ordine è già annullato.");
        if (o.Stato == StatoOrdine.Bozza)
            throw new OperazioneNonValidaException("La bozza non è ancora emessa: puoi eliminarla.");

        o.Stato = StatoOrdine.Annullato;
        o.MotivoAnnullamento = motivo.Trim();
        o.AnnullatoIl = Oggi();
        db.NotaStorico = $"Annullamento dell'ordine {o.Numero}: {o.MotivoAnnullamento}";
        await db.SaveChangesAsync(ct);
        await AggiornaPacchettiAsync(db, commessaId, PacchettiDi(o).ToList(), ct);
        await transazione.CommitAsync(ct);
    }

    /// <summary>Date facoltative dopo l'emissione: invio al fornitore e conferma d'ordine (con il PDF dall'archivio).</summary>
    public async Task RegistraInvioAsync(Guid commessaId, Guid ordineId, DateOnly? inviatoIl, DateOnly? confermatoIl, Guid? documentoId,
        CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var o = await db.Ordini.FirstOrDefaultAsync(x => x.Id == ordineId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("L'ordine non esiste.");
        if (o.Stato != StatoOrdine.Emesso) throw new OperazioneNonValidaException("Invio e conferma si registrano sugli ordini emessi.");
        o.InviatoIl = inviatoIl;
        o.ConfermatoIl = confermatoIl;
        o.ConfermaDocumentoId = documentoId;
        db.NotaStorico = $"Invio e conferma dell'ordine {o.Numero}";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stato del costo dei pacchetti dopo un'emissione o un annullamento (decisione 2): Definitivo quando tutte le righe
    /// sono ordinate, altrimenti Provvisorio; il riferimento ODA elenca gli ordini emessi.
    /// </summary>
    private static async Task AggiornaPacchettiAsync(PreventivoDbContext db, Guid commessaId, IReadOnlyCollection<Guid> pacchetti, CancellationToken ct)
    {
        if (pacchetti.Count == 0) return;
        var avanzamento = await AvanzamentoOrdini.CaricaAsync(db, commessaId, ct);
        foreach (var p in await db.Pacchetti.Where(p => pacchetti.Contains(p.Id)).ToListAsync(ct))
        {
            var a = avanzamento.Di(p.Id);
            var numeri = a?.Ordini ?? [];
            if (numeri.Count > 0)
            {
                p.StatoCosto = a!.TuttoOrdinato ? StatoCosto.Definitivo : StatoCosto.Provvisorio;
                var riferimento = string.Join(", ", numeri);
                p.RiferimentoOda = riferimento.Length <= 200 ? riferimento : riferimento[..197] + "…";
            }
            else if (p.RiferimentoOda?.StartsWith("ODA ", StringComparison.Ordinal) == true)
            {
                p.RiferimentoOda = null;
                if (p.StatoCosto == StatoCosto.Definitivo) p.StatoCosto = StatoCosto.Provvisorio;
            }
        }
        db.NotaStorico = "Stato del costo aggiornato dagli ordini";
        await db.SaveChangesAsync(ct);
    }

    // ---- Totali della scheda Ordini ----

    public async Task<RiepilogoAcquisti> RiepilogoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        var nomi = await db.Pacchetti.AsNoTracking().Where(p => p.CommessaId == commessaId)
            .ToDictionaryAsync(p => p.Id, p => (p.Ordine, p.Descrizione), ct);
        var emessi = await db.Ordini.AsNoTracking().Where(o => o.CommessaId == commessaId && o.Stato == StatoOrdine.Emesso).ToListAsync(ct);

        var perPacchetto = lista.Pacchetti.Values
            .OrderBy(a => nomi.TryGetValue(a.PacchettoId, out var n) ? n.Ordine : int.MaxValue)
            .Select(a => new TotaleAcquisti(nomi.TryGetValue(a.PacchettoId, out var n) ? n.Descrizione : "(pacchetto eliminato)",
                a.CostoPianificato, a.ImportoOrdinato, a.Residuo))
            .ToList();
        var fornitori = lista.Righe.Select(r => r.Fornitore ?? RigaSpesa.DaAssegnare).Concat(emessi.Select(o => o.Fornitore))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase);
        var perFornitore = fornitori.Select(f => new TotaleAcquisti(f,
                lista.Righe.Where(r => string.Equals(r.Fornitore ?? RigaSpesa.DaAssegnare, f, StringComparison.CurrentCultureIgnoreCase)).Sum(r => r.Importo),
                emessi.Where(o => string.Equals(o.Fornitore, f, StringComparison.CurrentCultureIgnoreCase)).Sum(o => o.Totale),
                lista.Righe.Where(r => string.Equals(r.Fornitore ?? RigaSpesa.DaAssegnare, f, StringComparison.CurrentCultureIgnoreCase)).Sum(AvanzamentoOrdini.Residuo)))
            .ToList();
        return new RiepilogoAcquisti(lista.Totale, emessi.Sum(o => o.Totale), lista.TotaleDaOrdinare, perPacchetto, perFornitore);
    }

    // ---- Supporto ----

    /// <summary>Quantità ancora da ordinare per chiave della Lista della Spesa.</summary>
    private static Dictionary<string, decimal> Residui(ListaSpesa lista) =>
        lista.Righe.GroupBy(r => r.ChiaveOrdine).ToDictionary(g => g.Key, g => g.Sum(r => r.DaOrdinare));

    /// <summary>Ripartisce la quantità di ogni riga sulle sue origini, fino al residuo di ciascuna; l'eccedenza va sull'ultima.</summary>
    internal static void Ripartisci(OrdineAcquisto o, IReadOnlyDictionary<string, decimal> residui)
    {
        foreach (var r in o.Righe.Where(r => r.Origini.Count > 0))
        {
            var resto = r.Quantita;
            foreach (var x in r.Origini)
            {
                var q = Math.Min(resto, Math.Max(0, residui.GetValueOrDefault(x.Chiave)));
                x.Quantita = q;
                resto -= q;
            }
            r.Origini[^1].Quantita += resto;
        }
    }

    private DateOnly Oggi() => DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);

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
