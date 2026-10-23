using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>
/// Riepilogo economico di commessa (regole R10–R12): ricavi, costi delle offerte scelte, MDC,
/// spese generali, risultato e obiettivo di margine, con gli avvisi essenziali.
/// </summary>
public sealed class RiepilogoService(IDbContextFactory<PreventivoDbContext> fabbrica, CommesseService commesse, TimeProvider orologio)
{
    /// <summary>Giorni di anticipo con cui si segnala la scadenza di un'offerta scelta.</summary>
    public const int GiorniPreavvisoScadenza = 15;

    public async Task<Fotografia> FotografiaAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await FotografiaAsync(db, commessaId, ct);
    }

    internal async Task<Fotografia> FotografiaAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var ribasso = preventivo.RibassoPercentuale;
        var computo = await db.Computi.AsNoTracking().FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);
        var vociComputo = computo is null ? [] : await db.VociComputo.AsNoTracking()
            .Where(v => v.ComputoId == computo.Id && !v.Archiviata).OrderBy(v => v.Ordine).ToListAsync(ct);
        var pacchetti = await db.Pacchetti.AsNoTracking().Include(p => p.Voci)
            .Where(p => p.CommessaId == commessaId && !p.Archiviato).OrderBy(p => p.Ordine).ToListAsync(ct);
        var offerte = await PacchettiService.CaricaOfferteAsync(db, commessaId, ct);
        var integrazioni = await db.IntegrazioniRicavo.AsNoTracking().Where(i => i.CommessaId == commessaId)
            .OrderBy(i => i.CreatoIl).ToListAsync(ct);
        var spese = await db.SpeseGenerali.AsNoTracking().Where(s => s.CommessaId == commessaId)
            .OrderBy(s => s.Ordine).ToListAsync(ct);
        var valoreContrattuale = (await commesse.OttieniAsync(commessaId, ct))?.ValoreContrattuale;

        var quadro = QuadroEconomico.Calcola(ribasso, vociComputo.Select(v => (v.Quantita, v.Prezzo, v.PercentualeNonSoggetta)), valoreContrattuale);
        var perId = vociComputo.ToDictionary(v => v.Id,
            v => new VoceDiCalcolo(v.Id, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo, v.PercentualeNonSoggetta));
        var posizione = vociComputo.Select((v, i) => (v.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var avanzamento = await AvanzamentoOrdini.CaricaAsync(db, commessaId, ct);

        // Ricavo e costo per voce secondo l'offerta scelta del suo pacchetto.
        var pacchettoDi = new Dictionary<Guid, Guid>();
        var dettaglioVoce = new Dictionary<Guid, VoceOffertaCalcolata>();
        var righePacchetti = new List<PacchettoFotografia>();
        var integrazioniPerPacchetto = integrazioni.Where(i => i.PacchettoId is not null)
            .GroupBy(i => i.PacchettoId!.Value).ToDictionary(g => g.Key, g => g.Sum(i => i.Importo));
        foreach (var p in pacchetti)
        {
            var voci = p.Voci.Where(v => perId.ContainsKey(v.VoceComputoId)).Select(v => perId[v.VoceComputoId])
                .OrderBy(v => posizione[v.Id]).ToList();
            foreach (var v in voci) pacchettoDi[v.Id] = p.Id;
            var offertePacchetto = offerte.Where(o => o.PacchettoId == p.Id).ToList();
            var scelta = offertePacchetto.FirstOrDefault(o => o.Id == p.OffertaSceltaId);
            decimal ricavo;
            decimal? costo = null;
            if (scelta is not null)
            {
                var r = CalcoloPacchetti.Calcola(scelta, voci, ribasso);
                foreach (var vc in r.Voci) dettaglioVoce[vc.Voce.Id] = vc;
                (ricavo, costo) = (r.RicavoNetto, r.Costo);
            }
            else
            {
                ricavo = CalcoloPacchetti.RicavoDaComputo(voci, ribasso).Netto;
            }
            // Decisione 1 del Modulo 2: con ordini emessi il costo del pacchetto è l'importo degli ordini più il residuo pianificato.
            var a = avanzamento.Di(p.Id);
            var attuale = a is { HaOrdini: true } ? a.CostoAttuale : costo;
            righePacchetti.Add(new PacchettoFotografia(p.Id, p.Ordine, p.Descrizione, p.Tipo, p.StatoCosto,
                scelta?.NomeFornitore, scelta?.Numero, scelta?.Data, Scadenza(scelta), voci.Count, offertePacchetto.Count,
                ricavo, integrazioniPerPacchetto.GetValueOrDefault(p.Id), attuale, costo, a?.ImportoOrdinato ?? 0, a?.PercentualeOrdinata ?? 0));
        }

        var vociFoto = vociComputo.Select(v =>
        {
            dettaglioVoce.TryGetValue(v.Id, out var d);
            var netto = d?.Ricavo.Netto ?? CalcoloRicavi.Voce(v.Quantita, v.Prezzo, ribasso, v.PercentualeNonSoggetta).Netto;
            return new VoceFotografia(v.Id, v.Ordine, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo,
                v.PercentualeNonSoggetta, netto, pacchettoDi.TryGetValue(v.Id, out var pid) ? pid : null,
                d is { QuantitaModificata: true } ? d.Quantita : null, d?.Costo) { Wbs = v.CodiceWbs };
        }).ToList();

        var libere = vociFoto.Where(v => v.PacchettoId is null).ToList();
        var nomePacchetto = pacchetti.ToDictionary(p => p.Id, p => p.Descrizione);
        var integrazioniCommessa = integrazioni.Where(i => i.PacchettoId is null || !nomePacchetto.ContainsKey(i.PacchettoId.Value)).Sum(i => i.Importo);
        var senzaCosto = righePacchetti.Where(p => p.Costo is null).ToList();

        var parziale = new TotaliFotografia(quadro.ImportoLavoriLordo, quadro.ImportoNonSoggetto, quadro.ImportoRibasso,
            quadro.ImportoNetto, valoreContrattuale,
            righePacchetti.Sum(p => p.Ricavo), libere.Sum(v => v.RicavoNetto), libere.Count, integrazioniCommessa,
            righePacchetti.Sum(p => p.Costo ?? 0), senzaCosto.Sum(p => p.Ricavo) + libere.Sum(v => v.RicavoNetto), senzaCosto.Count, 0);

        var speseFoto = spese.Select(s => new SpesaFotografia(s.Id, s.Descrizione, s.Modo, s.Importo, s.Percentuale, s.Base,
            ImportoSpesa(s.Modo, s.Importo, s.Percentuale, s.Base, parziale), s.Note)).ToList();

        return new Fotografia(orologio.GetUtcNow().UtcDateTime, ribasso, preventivo.ObiettivoMarginePercentuale,
            parziale with { SpeseGenerali = speseFoto.Sum(s => s.ImportoCalcolato) },
            vociFoto, righePacchetti, speseFoto,
            integrazioni.Select(i => new IntegrazioneFotografia(i.Id, i.Descrizione, i.PacchettoId,
                i.PacchettoId is { } id ? nomePacchetto.GetValueOrDefault(id) : null, i.Importo, i.Note)).ToList());
    }

    /// <summary>Importo di una spesa generale; la percentuale si applica alla base scelta, prima delle spese stesse.</summary>
    public static decimal ImportoSpesa(ModoSpesaGenerale modo, decimal importo, decimal percentuale, BaseSpesaGenerale baseSpesa, TotaliFotografia totali) =>
        modo == ModoSpesaGenerale.Importo
            ? importo
            : Importi.Arrotonda(percentuale / 100m * baseSpesa switch
            {
                BaseSpesaGenerale.CostoCommessa => totali.Costo,
                BaseSpesaGenerale.ImportoLavoriLordo => totali.ImportoLavoriLordo,
                _ => totali.Ricavo,
            });

    // ---- Avvisi essenziali ----

    public async Task<IReadOnlyList<Avviso>> AvvisiAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var f = await FotografiaAsync(db, commessaId, ct);
        var computo = await db.Computi.AsNoTracking().FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);
        var ultima = await db.Versioni.AsNoTracking().Where(v => v.CommessaId == commessaId)
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
        return Avvisi(f, computo, ultima, DateOnly.FromDateTime(orologio.GetLocalNow().DateTime));
    }

    internal static List<Avviso> Avvisi(Fotografia f, Computo? computo, VersionePreventivo? ultima, DateOnly oggi)
    {
        var avvisi = new List<Avviso>();
        var t = f.Totali;

        foreach (var p in f.Pacchetti.Where(p => p.Mdc < 0))
            avvisi.Add(new(GravitaAvviso.Errore, $"Il pacchetto «{p.Descrizione}» è in perdita: MDC {Formato.Euro(p.Mdc)}.",
                "Verifica l'offerta scelta o le quantità di pianificazione."));

        foreach (var p in f.Pacchetti.Where(p => p.ScadenzaOfferta is not null))
        {
            var scadenza = p.ScadenzaOfferta!.Value;
            if (scadenza < oggi)
                avvisi.Add(new(GravitaAvviso.Attenzione,
                    $"L'offerta scelta per «{p.Descrizione}» ({p.Fornitore}) è scaduta il {Formato.Data(scadenza)}.",
                    "Chiedi al fornitore la conferma dei prezzi o una nuova offerta."));
            else if (scadenza <= oggi.AddDays(GiorniPreavvisoScadenza))
                avvisi.Add(new(GravitaAvviso.Informazione,
                    $"L'offerta scelta per «{p.Descrizione}» ({p.Fornitore}) scade il {Formato.Data(scadenza)}.",
                    "Valuta se confermare l'ordine prima della scadenza."));
        }

        if (t.PacchettiSenzaCosto > 0)
            avvisi.Add(new(GravitaAvviso.Attenzione,
                $"{t.PacchettiSenzaCosto} pacchetti senza offerta scelta: costo non pianificato.",
                "Scegli un'offerta (anche una stima interna) per ogni pacchetto."));
        if (t.VociNonAssegnate > 0)
            avvisi.Add(new(GravitaAvviso.Attenzione,
                $"{t.VociNonAssegnate} voci del computo non sono assegnate a un pacchetto (ricavo {Formato.Euro(t.RicavoNonAssegnato)}).",
                "Assegnale a un pacchetto dalla scheda Pacchetti: oggi il loro costo non è pianificato."));

        var senzaPrezzo = f.Voci.Count(v => v.Prezzo == 0);
        if (senzaPrezzo > 0)
            avvisi.Add(new(GravitaAvviso.Attenzione, $"{senzaPrezzo} voci del computo hanno prezzo zero.",
                "Completa i prezzi nella scheda Preventivo."));

        if (f.ObiettivoMarginePercentuale is { } obiettivo)
        {
            if (t.MarginePercentuale is { } m && m < obiettivo)
                avvisi.Add(new(GravitaAvviso.Attenzione,
                    $"Il margine di commessa ({Formato.Percento(m)}) è sotto l'obiettivo del {Formato.Percento(obiettivo)}.",
                    "Controlla i pacchetti con il margine più basso."));
            var sotto = f.Pacchetti.Where(p => p.MarginePercentuale is { } mp && mp >= 0 && mp < obiettivo).ToList();
            if (sotto.Count > 0)
                avvisi.Add(new(GravitaAvviso.Informazione,
                    $"{sotto.Count} pacchetti hanno un margine sotto l'obiettivo: {string.Join(", ", sotto.Take(5).Select(p => p.Descrizione))}{(sotto.Count > 5 ? " e altri" : "")}.",
                    "Valuta offerte alternative o una nuova trattativa."));
        }

        if (computo?.TotaleDichiarato is { } dichiarato && Math.Abs(dichiarato - t.ImportoLavoriLordo) >= 0.01m)
            avvisi.Add(new(GravitaAvviso.Attenzione,
                $"Il totale del computo calcolato ({Formato.Euro(t.ImportoLavoriLordo)}) differisce dal totale del documento ({Formato.Euro(dichiarato)}).",
                "Verifica le voci del computo prima di pubblicare una versione."));

        if (t.ValoreContrattuale is { } contratto && Math.Abs(contratto - t.RicavoNettoComputo) >= 0.01m)
            avvisi.Add(new(GravitaAvviso.Informazione,
                $"Il valore contrattuale della commessa ({Formato.Euro(contratto)}) è diverso dal ricavo netto del computo ({Formato.Euro(t.RicavoNettoComputo)}).",
                "È solo una segnalazione: il valore contrattuale non entra nei calcoli."));

        if (ultima is not null && DiversaDallaVersione(f, ultima))
            avvisi.Add(new(GravitaAvviso.Informazione,
                $"La bozza è cambiata rispetto alla versione ufficiale n. {ultima.Numero}.",
                "Pubblica una nuova versione dalla scheda Versioni quando le modifiche sono definitive."));

        return avvisi;
    }

    private static bool DiversaDallaVersione(Fotografia f, VersionePreventivo v) =>
        Importi.Arrotonda(f.Totali.Ricavo) != Importi.Arrotonda(v.Ricavo)
        || Importi.Arrotonda(f.Totali.Costo) != Importi.Arrotonda(v.Costo)
        || Importi.Arrotonda(f.Totali.SpeseGenerali) != Importi.Arrotonda(v.SpeseGenerali)
        || f.RibassoPercentuale != v.RibassoPercentuale;

    private static DateOnly? Scadenza(Offerta? o) =>
        o is null ? null : o.Scadenza ?? (o.Data is { } d && o.ValiditaGiorni is { } g ? d.AddDays(g) : null);

    // ---- Modifiche: obiettivo, spese generali, integrazioni ----

    public async Task ImpostaObiettivoAsync(Guid commessaId, decimal? percentuale, CancellationToken ct = default)
    {
        if (percentuale is < 0 or >= 100)
            throw new OperazioneNonValidaException("L'obiettivo di margine deve essere compreso tra 0 e 100%.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        p.ObiettivoMarginePercentuale = percentuale;
        db.NotaStorico = "Obiettivo di margine";
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SpesaGenerale>> SpeseGeneraliAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.SpeseGenerali.AsNoTracking().Where(s => s.CommessaId == commessaId).OrderBy(s => s.Ordine).ToListAsync(ct);
    }

    public async Task SalvaSpesaAsync(Guid commessaId, SpesaGenerale dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Descrizione))
            throw new OperazioneNonValidaException("La descrizione della spesa generale è obbligatoria.");
        if (dati.Modo == ModoSpesaGenerale.Percentuale && dati.Percentuale is <= 0 or >= 100)
            throw new OperazioneNonValidaException("La percentuale deve essere maggiore di 0 e minore di 100.");
        if (dati.Modo == ModoSpesaGenerale.Importo && dati.Importo < 0)
            throw new OperazioneNonValidaException("L'importo non può essere negativo.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        SpesaGenerale s;
        if (dati.Id == Guid.Empty || !await db.SpeseGenerali.AnyAsync(x => x.Id == dati.Id, ct))
        {
            var ultimo = await db.SpeseGenerali.Where(x => x.CommessaId == commessaId).MaxAsync(x => (int?)x.Ordine, ct) ?? 0;
            s = new SpesaGenerale { CommessaId = commessaId, Ordine = ultimo + 1 };
            db.SpeseGenerali.Add(s);
        }
        else
        {
            s = await db.SpeseGenerali.FirstAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct);
            db.Entry(s).Property(x => x.Versione).OriginalValue = dati.Versione;
        }
        s.Descrizione = dati.Descrizione.Trim();
        s.Modo = dati.Modo;
        s.Importo = dati.Modo == ModoSpesaGenerale.Importo ? dati.Importo : 0;
        s.Percentuale = dati.Modo == ModoSpesaGenerale.Percentuale ? dati.Percentuale : 0;
        s.Base = dati.Base;
        s.Note = string.IsNullOrWhiteSpace(dati.Note) ? null : dati.Note.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task EliminaSpesaAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var s = await db.SpeseGenerali.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct);
        if (s is null) return;
        db.SpeseGenerali.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<IntegrazioneRicavo>> IntegrazioniAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.IntegrazioniRicavo.AsNoTracking().Where(i => i.CommessaId == commessaId).OrderBy(i => i.CreatoIl).ToListAsync(ct);
    }

    public async Task SalvaIntegrazioneAsync(Guid commessaId, IntegrazioneRicavo dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Descrizione))
            throw new OperazioneNonValidaException("La descrizione dell'integrazione è obbligatoria.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        if (dati.PacchettoId is { } pid && !await db.Pacchetti.AnyAsync(p => p.Id == pid && p.CommessaId == commessaId, ct))
            throw new OperazioneNonValidaException("Il pacchetto indicato non esiste.");
        IntegrazioneRicavo i;
        if (dati.Id == Guid.Empty || !await db.IntegrazioniRicavo.AnyAsync(x => x.Id == dati.Id, ct))
        {
            i = new IntegrazioneRicavo { CommessaId = commessaId };
            db.IntegrazioniRicavo.Add(i);
        }
        else
        {
            i = await db.IntegrazioniRicavo.FirstAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct);
            db.Entry(i).Property(x => x.Versione).OriginalValue = dati.Versione;
        }
        i.Descrizione = dati.Descrizione.Trim();
        i.PacchettoId = dati.PacchettoId;
        i.Importo = dati.Importo;
        i.Note = string.IsNullOrWhiteSpace(dati.Note) ? null : dati.Note.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task EliminaIntegrazioneAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var i = await db.IntegrazioniRicavo.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct);
        if (i is null) return;
        db.IntegrazioniRicavo.Remove(i);
        await db.SaveChangesAsync(ct);
    }
}
