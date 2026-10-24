using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Esportazioni;
using Corico.Modules.Preventivo.Interfaccia;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record RigaElencoQualifiche(
    Guid Id, string Fornitore, TipoOperatore Tipo, string? OggettoPrestazioni, StatoQualifica Stato, DateOnly? DataConclusione,
    string? DirettoreCantiere, StatoAutorizzazione Autorizzazione, int Documenti, int Verificati,
    Guid? ComunicazioneId, string? NumeroComunicazione, StatoComunicazione? StatoComunicazione);

/// <summary>Esito dei controlli prima del PDF definitivo: gli impedimenti bloccano, gli avvisi no.</summary>
public sealed record VerificaComunicazione(IReadOnlyList<string> Impedimenti, IReadOnlyList<string> Avvisi, bool AziendaCompilata)
{
    public bool Generabile => Impedimenti.Count == 0;
}

/// <summary>Dati della lettera già risolti, per l'anteprima e per il PDF definitivo.</summary>
public sealed record LetteraQualifica(
    DatiAzienda Azienda, DatiCommessa Commessa, string? Committente, QualificaFornitore Qualifica, ComunicazioneQualifica Comunicazione,
    DatiFornitore? Fornitore);

/// <summary>
/// Qualificazione interna dei fornitori per commessa e comunicazione dell'esito positivo a DL, RUP e CSE (richiesta del 24/10/2026).
/// La comunicazione si prepara solo dopo l'approvazione del Direttore di cantiere; il PDF definitivo si archivia nel fascicolo
/// del fornitore e nei documenti della commessa; l'invio effettivo si registra a parte e non coincide con la generazione.
/// </summary>
public sealed class QualificheService(
    IDbContextFactory<PreventivoDbContext> fabbrica, CommesseService commesse, ClientiService clienti, FornitoriService fornitori,
    AttoriService attori, DocumentiService documenti, ImpostazioniService impostazioni, IUtenteCorrente utente, TimeProvider orologio)
{
    public const string TipoDocumentoQualifica = "Qualificazione fornitore";
    public const string TipoDocumentoComunicazione = "Comunicazione qualificazione";

    private static readonly SemaphoreSlim BloccoNumeri = new(1, 1);

    /// <summary>Ruoli destinatari della comunicazione, nell'ordine della lettera.</summary>
    public static readonly RuoloCommessa[] RuoliDestinatari = [RuoloCommessa.DirettoreLavori, RuoloCommessa.Rup, RuoloCommessa.Cse];

    // ---- Qualificazioni ----

    public async Task<IReadOnlyList<RigaElencoQualifiche>> ElencoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.AsNoTracking().Include(x => x.Documenti).Where(x => x.CommessaId == commessaId).ToListAsync(ct);
        var c = await db.ComunicazioniQualifica.AsNoTracking().Where(x => x.CommessaId == commessaId).ToListAsync(ct);
        return q.OrderBy(x => x.Fornitore, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.CreatoIl)
            .Select(x =>
            {
                var ultima = UltimaDi(c, x.Id);
                return new RigaElencoQualifiche(x.Id, x.Fornitore, x.Tipo, x.OggettoPrestazioni, x.Stato, x.DataConclusione, x.DirettoreCantiere,
                    x.Autorizzazione, x.Documenti.Count, x.Documenti.Count(d => d.Verificato), ultima?.Id, ultima?.NumeroVisibile, ultima?.Stato);
            }).ToList();
    }

    /// <summary>Qualificazioni del fornitore in tutte le commesse (fascicolo del fornitore).</summary>
    public async Task<IReadOnlyList<(QualificaFornitore Qualifica, IReadOnlyList<ComunicazioneQualifica> Comunicazioni)>> FascicoloAsync(
        Guid fornitoreId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.AsNoTracking().Include(x => x.Documenti).Where(x => x.FornitoreId == fornitoreId).ToListAsync(ct);
        var ids = q.Select(x => x.Id).ToList();
        var c = await db.ComunicazioniQualifica.AsNoTracking().Include(x => x.Allegati).Where(x => ids.Contains(x.QualificaId)).ToListAsync(ct);
        foreach (var x in q) x.Documenti.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        return q.OrderByDescending(x => x.DataConclusione ?? DateOnly.FromDateTime(x.CreatoIl))
            .Select(x => (x, (IReadOnlyList<ComunicazioneQualifica>)c.Where(k => k.QualificaId == x.Id).OrderByDescending(k => k.CreatoIl).ToList()))
            .ToList();
    }

    public async Task<QualificaFornitore?> OttieniAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.AsNoTracking().Include(x => x.Documenti).FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct);
        q?.Documenti.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        return q;
    }

    /// <summary>Nuova qualificazione in istruttoria, con l'elenco dei documenti tipici per il tipo di operatore.</summary>
    public async Task<Guid> CreaAsync(Guid commessaId, Guid fornitoreId, TipoOperatore tipo, Guid? pacchettoId, CancellationToken ct = default)
    {
        PuoModificare();
        var f = await fornitori.OttieniAsync(fornitoreId, ct) ?? throw new OperazioneNonValidaException("Scegli il fornitore dall'anagrafica.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = pacchettoId is null ? null
            : await db.Pacchetti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pacchettoId && x.CommessaId == commessaId, ct);
        var q = new QualificaFornitore
        {
            CommessaId = commessaId, FornitoreId = f.Id, Fornitore = f.Denominazione.Trim(), Tipo = tipo, PacchettoId = p?.Id,
            OggettoPrestazioni = p?.Descrizione,
            Autorizzazione = tipo is TipoOperatore.Subappaltatore or TipoOperatore.SubAffidatario ? StatoAutorizzazione.DaRichiedere : StatoAutorizzazione.NonNecessaria,
        };
        var n = 1;
        foreach (var d in DocumentiTipici(tipo))
            q.Documenti.Add(new DocumentoQualifica { QualificaId = q.Id, CommessaId = commessaId, Ordine = n++, Descrizione = d });
        db.Qualifiche.Add(q);
        db.NotaStorico = $"Nuova qualificazione di {q.Fornitore}";
        await db.SaveChangesAsync(ct);
        return q.Id;
    }

    /// <summary>Pacchetti della commessa con il tipo di affidamento, per proporre oggetto e tipologia della qualificazione.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Descrizione, TipoAffidamento Tipo)>> PacchettiAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var p = await db.Pacchetti.AsNoTracking().Where(x => x.CommessaId == commessaId && !x.Archiviato).OrderBy(x => x.Ordine)
            .Select(x => new { x.Id, x.Descrizione, x.Tipo }).ToListAsync(ct);
        return p.Select(x => (x.Id, x.Descrizione, x.Tipo)).ToList();
    }

    /// <summary>Tipo di operatore proposto dal tipo di affidamento del pacchetto.</summary>
    public static TipoOperatore TipoDa(TipoAffidamento a) => a switch
    {
        TipoAffidamento.Subappalto => TipoOperatore.Subappaltatore,
        TipoAffidamento.NoloACaldo or TipoAffidamento.FornituraEPosa => TipoOperatore.SubAffidatario,
        TipoAffidamento.LavoriInEconomia => TipoOperatore.ImpresaEsecutrice,
        _ => TipoOperatore.Fornitore,
    };

    /// <summary>Documenti proposti all'apertura della qualificazione: si tolgono o si aggiungono liberamente.</summary>
    public static IReadOnlyList<string> DocumentiTipici(TipoOperatore tipo)
    {
        var d = new List<string>
        {
            "Visura camerale aggiornata",
            "DURC in corso di validità",
            "Dichiarazione sul possesso dei requisiti generali (artt. 94-98 D.Lgs. 36/2023)",
            "Dichiarazione di tracciabilità dei flussi finanziari (L. 136/2010)",
        };
        if (tipo != TipoOperatore.Fornitore)
        {
            d.Add("Idoneità tecnico-professionale (art. 90 e All. XVII D.Lgs. 81/2008)");
            d.Add("Piano operativo di sicurezza (POS)");
            d.Add("Polizza di responsabilità civile verso terzi e prestatori d'opera");
            d.Add("Attestazione SOA o requisiti di qualificazione, se richiesti");
        }
        return d;
    }

    /// <summary>Salva la scheda in istruttoria (dati, documenti verificati). Dopo l'esito cambia solo lo stato dell'autorizzazione.</summary>
    public async Task SalvaAsync(Guid commessaId, QualificaFornitore dati, CancellationToken ct = default)
    {
        PuoModificare();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.Include(x => x.Documenti).FirstOrDefaultAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La qualificazione non esiste più.");
        db.Entry(q).Property(x => x.Versione).OriginalValue = dati.Versione;

        q.Autorizzazione = dati.Autorizzazione;
        q.RiferimentoAutorizzazione = Pulisci(dati.RiferimentoAutorizzazione);
        q.DataAutorizzazione = dati.DataAutorizzazione;
        if (q.Stato == StatoQualifica.InIstruttoria)
        {
            q.Tipo = dati.Tipo;
            q.PacchettoId = dati.PacchettoId;
            q.OggettoPrestazioni = Pulisci(dati.OggettoPrestazioni);
            q.RiferimentoFascicolo = Pulisci(dati.RiferimentoFascicolo);
            q.Prescrizioni = Pulisci(dati.Prescrizioni);
            q.Note = Pulisci(dati.Note);
            if (await fornitori.OttieniAsync(q.FornitoreId, ct) is { } f) q.Fornitore = f.Denominazione.Trim();

            var arrivati = dati.Documenti.ToDictionary(d => d.Id);
            foreach (var d in q.Documenti.ToList())
            {
                if (!arrivati.Remove(d.Id, out var n))
                {
                    q.Documenti.Remove(d);
                    db.DocumentiQualifica.Remove(d);
                    continue;
                }
                Copia(n, d);
            }
            foreach (var n in arrivati.Values)
            {
                var d = new DocumentoQualifica { QualificaId = q.Id, CommessaId = commessaId };
                Copia(n, d);
                q.Documenti.Add(d);
                db.DocumentiQualifica.Add(d);
            }
            var numero = 1;
            foreach (var d in q.Documenti.OrderBy(d => d.Ordine)) d.Ordine = numero++;
            if (q.Documenti.Any(d => d.Descrizione.Length == 0))
                throw new OperazioneNonValidaException("Ogni documento verificato deve avere una descrizione.");
        }
        db.NotaStorico = $"Qualificazione di {q.Fornitore}";
        await db.SaveChangesAsync(ct);

        static void Copia(DocumentoQualifica da, DocumentoQualifica a)
        {
            a.Ordine = da.Ordine;
            a.Descrizione = (da.Descrizione ?? "").Trim();
            a.Verificato = da.Verificato;
            a.Scadenza = da.Scadenza;
            a.DocumentoId = da.DocumentoId;
            a.Riservato = da.Riservato;
        }
    }

    /// <summary>Controlli prima dell'approvazione: almeno un documento, tutti verificati.</summary>
    public static IReadOnlyList<string> ImpedimentiApprovazione(QualificaFornitore q)
    {
        var i = new List<string>();
        if (q.Stato != StatoQualifica.InIstruttoria) i.Add("La qualificazione ha già un esito.");
        if (q.Documenti.Count == 0) i.Add("Indica almeno un documento verificato.");
        foreach (var d in q.Documenti.Where(d => !d.Verificato))
            i.Add($"Documento «{d.Descrizione}»: non è segnato come verificato (verificalo o toglilo dall'elenco).");
        return i;
    }

    /// <summary>
    /// Registra l'approvazione del Direttore di cantiere con esito positivo e genera la bozza della comunicazione a DL, RUP e CSE.
    /// Restituisce l'identificativo della bozza.
    /// </summary>
    public async Task<Guid> ApprovaAsync(Guid commessaId, Guid id, DateOnly dataConclusione, Guid? direttoreId, string? direttore,
        CancellationToken ct = default)
    {
        PuoModificare();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.Include(x => x.Documenti).FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La qualificazione non esiste più.");
        if (ImpedimentiApprovazione(q) is { Count: > 0 } impedimenti) throw new OperazioneNonValidaException(impedimenti[0]);
        var nome = await NomeDirettoreAsync(commessaId, direttoreId, direttore, dataConclusione, ct);

        q.Stato = StatoQualifica.Approvata;
        q.DataConclusione = dataConclusione;
        q.DirettoreCantiereId = direttoreId;
        q.DirettoreCantiere = nome;
        q.ApprovataDa = utente.Nome;
        q.ApprovataIl = orologio.GetUtcNow().UtcDateTime;
        db.NotaStorico = $"Qualificazione di {q.Fornitore} approvata da {nome}";
        db.RegistraOperazione("Approvazione qualificazione", nameof(QualificaFornitore), q.Id, commessaId,
            $"{q.Fornitore}: esito positivo, Direttore di cantiere {nome}", dopo: new { q.Fornitore, q.DataConclusione, q.DirettoreCantiere });
        await db.SaveChangesAsync(ct);
        return await CreaBozzaAsync(db, q, ct);
    }

    /// <summary>Esito negativo: la qualificazione si chiude senza comunicazione. Il motivo va nelle note.</summary>
    public async Task EsitoNegativoAsync(Guid commessaId, Guid id, DateOnly dataConclusione, Guid? direttoreId, string? direttore, string motivo,
        CancellationToken ct = default)
    {
        PuoModificare();
        if (string.IsNullOrWhiteSpace(motivo)) throw new OperazioneNonValidaException("Indica il motivo dell'esito negativo.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La qualificazione non esiste più.");
        if (q.Stato != StatoQualifica.InIstruttoria) throw new OperazioneNonValidaException("La qualificazione ha già un esito.");
        var nome = await NomeDirettoreAsync(commessaId, direttoreId, direttore, dataConclusione, ct);
        q.Stato = StatoQualifica.NonQualificato;
        q.DataConclusione = dataConclusione;
        q.DirettoreCantiereId = direttoreId;
        q.DirettoreCantiere = nome;
        q.ApprovataDa = utente.Nome;
        q.ApprovataIl = orologio.GetUtcNow().UtcDateTime;
        q.Note = string.Join("\n", new[] { Pulisci(q.Note), "Esito negativo: " + motivo.Trim() }.Where(x => x is not null));
        db.NotaStorico = $"Qualificazione di {q.Fornitore}: esito negativo";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Riporta in istruttoria una qualificazione con esito (solo amministratore), purché nessuna comunicazione sia stata
    /// generata in definitivo: le bozze si eliminano.
    /// </summary>
    public async Task RiapriAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        if (!UtenteCorrente.PuoAmministrare) throw new OperazioneNonValidaException("Riaprire una qualificazione è riservato all'amministratore.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La qualificazione non esiste più.");
        var com = await db.ComunicazioniQualifica.Where(x => x.QualificaId == id).ToListAsync(ct);
        if (com.Any(x => x.Stato != StatoComunicazione.Bozza && x.Stato != StatoComunicazione.Annullata))
            throw new OperazioneNonValidaException("La comunicazione è già stata generata in definitivo: annullala prima di riaprire la qualificazione.");
        db.ComunicazioniQualifica.RemoveRange(com.Where(x => x.Stato == StatoComunicazione.Bozza));
        q.Stato = StatoQualifica.InIstruttoria;
        q.ApprovataDa = null;
        q.ApprovataIl = null;
        db.NotaStorico = $"Qualificazione di {q.Fornitore} riaperta";
        await db.SaveChangesAsync(ct);
    }

    public async Task EliminaAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        PuoModificare();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La qualificazione non esiste più.");
        if (q.Stato != StatoQualifica.InIstruttoria) throw new OperazioneNonValidaException("Si eliminano solo le qualificazioni in istruttoria.");
        db.Qualifiche.Remove(q);
        db.NotaStorico = $"Eliminata la qualificazione di {q.Fornitore}";
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> NomeDirettoreAsync(Guid commessaId, Guid? direttoreId, string? direttore, DateOnly data, CancellationToken ct)
    {
        if (direttoreId is { } aid)
        {
            var figure = await attori.FigureInCaricaAsync(commessaId, data, RuoloCommessa.DirettoreCantiere, ct);
            var f = figure.FirstOrDefault(x => x.AttoreId == aid) ?? (await attori.FigureInCaricaAsync(commessaId, null, RuoloCommessa.DirettoreCantiere, ct))
                .FirstOrDefault(x => x.AttoreId == aid);
            if (f is not null) return Nominativo(f.Titolo, f.Nominativo);
        }
        return Pulisci(direttore) ?? throw new OperazioneNonValidaException(
            "Indica il Direttore di cantiere che ha curato la qualificazione (dagli Attori della commessa o scrivendo il nominativo).");
    }

    // ---- Comunicazioni ----

    public async Task<IReadOnlyList<ComunicazioneQualifica>> ComunicazioniAsync(Guid commessaId, Guid qualificaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.ComunicazioniQualifica.AsNoTracking().Where(x => x.CommessaId == commessaId && x.QualificaId == qualificaId)
            .OrderByDescending(x => x.CreatoIl).ToListAsync(ct);
    }

    public async Task<ComunicazioneQualifica?> ComunicazioneAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.ComunicazioniQualifica.AsNoTracking().Include(x => x.Destinatari).Include(x => x.Allegati)
            .FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct);
        if (c is not null)
        {
            c.Destinatari.Sort((a, b) => Array.IndexOf(RuoliDestinatari, a.Ruolo).CompareTo(Array.IndexOf(RuoliDestinatari, b.Ruolo)));
            c.Allegati.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
        }
        return c;
    }

    /// <summary>Nuova bozza dopo l'annullamento della precedente (la qualificazione deve essere approvata).</summary>
    public async Task<Guid> NuovaBozzaAsync(Guid commessaId, Guid qualificaId, CancellationToken ct = default)
    {
        PuoModificare();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var q = await db.Qualifiche.AsNoTracking().Include(x => x.Documenti).FirstOrDefaultAsync(x => x.Id == qualificaId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La qualificazione non esiste più.");
        if (q.Stato != StatoQualifica.Approvata)
            throw new OperazioneNonValidaException("La comunicazione si prepara solo dopo l'approvazione della qualificazione da parte del Direttore di cantiere.");
        if (await db.ComunicazioniQualifica.AnyAsync(x => x.QualificaId == qualificaId && x.Stato != StatoComunicazione.Annullata, ct))
            throw new OperazioneNonValidaException("C'è già una comunicazione valida per questa qualificazione: annullala prima di prepararne una nuova.");
        return await CreaBozzaAsync(db, q, ct);
    }

    /// <summary>
    /// Bozza con i destinatari in carica dagli Attori della commessa: DL e RUP sempre selezionati, il CSE per chi opera in cantiere.
    /// Un ruolo senza attori ha una riga vuota da completare a mano. Nessun documento dell'archivio è allegato in automatico.
    /// </summary>
    private async Task<Guid> CreaBozzaAsync(PreventivoDbContext db, QualificaFornitore q, CancellationToken ct)
    {
        var c = new ComunicazioneQualifica
        {
            CommessaId = q.CommessaId, QualificaId = q.Id, DataEmissione = Oggi(), Prescrizioni = q.Prescrizioni,
            Oggetto = $"Comunicazione dell'esito della qualificazione interna – {Testi.TipoOperatore(q.Tipo)} {q.Fornitore}",
        };
        var figure = await attori.FigureInCaricaAsync(q.CommessaId, null, null, ct);
        foreach (var ruolo in RuoliDestinatari)
        {
            var selezionato = ruolo != RuoloCommessa.Cse || q.Tipo != TipoOperatore.Fornitore;
            var perRuolo = figure.Where(f => f.Ruolo == ruolo).ToList();
            if (perRuolo.Count == 0)
                c.Destinatari.Add(new DestinatarioComunicazione { ComunicazioneId = c.Id, CommessaId = c.CommessaId, Ruolo = ruolo, Selezionato = selezionato });
            foreach (var f in perRuolo)
                c.Destinatari.Add(new DestinatarioComunicazione
                {
                    ComunicazioneId = c.Id, CommessaId = c.CommessaId, Ruolo = ruolo, Selezionato = selezionato, AttoreId = f.AttoreId,
                    Titolo = Pulisci(f.Titolo), Nominativo = Pulisci(f.Nominativo), Ente = Pulisci(f.Ente), Pec = Pulisci(f.Pec), Email = Pulisci(f.Email),
                    Indirizzo = Pulisci(string.Join(" ", new[] { f.Indirizzo, f.Cap, f.Comune, f.Provincia is { Length: > 0 } pr ? $"({pr})" : null }
                        .Where(x => !string.IsNullOrWhiteSpace(x)))),
                });
        }
        db.ComunicazioniQualifica.Add(c);
        db.NotaStorico = $"Bozza della comunicazione di qualificazione di {q.Fornitore}";
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    /// <summary>Salva la bozza: data, oggetto, note, prescrizioni, destinatari (completati a mano) e allegati scelti.</summary>
    public async Task SalvaBozzaAsync(Guid commessaId, ComunicazioneQualifica dati, CancellationToken ct = default)
    {
        PuoModificare();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.ComunicazioniQualifica.Include(x => x.Destinatari).Include(x => x.Allegati)
            .FirstOrDefaultAsync(x => x.Id == dati.Id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La comunicazione non esiste più.");
        if (c.Stato != StatoComunicazione.Bozza) throw new OperazioneNonValidaException("Il PDF definitivo è già generato: la comunicazione non si modifica più.");
        db.Entry(c).Property(x => x.Versione).OriginalValue = dati.Versione;

        c.DataEmissione = dati.DataEmissione;
        c.Oggetto = (dati.Oggetto ?? "").Trim();
        c.Note = Pulisci(dati.Note);
        c.Prescrizioni = Pulisci(dati.Prescrizioni);
        c.AllegaAttestato = dati.AllegaAttestato;
        c.AllegaElencoDocumenti = dati.AllegaElencoDocumenti;

        var arrivati = dati.Destinatari.ToDictionary(d => d.Id);
        foreach (var d in c.Destinatari.ToList())
        {
            if (!arrivati.Remove(d.Id, out var n))
            {
                c.Destinatari.Remove(d);
                db.DestinatariComunicazione.Remove(d);
                continue;
            }
            Copia(n, d);
        }
        foreach (var n in arrivati.Values)
        {
            var d = new DestinatarioComunicazione { ComunicazioneId = c.Id, CommessaId = commessaId };
            Copia(n, d);
            c.Destinatari.Add(d);
            db.DestinatariComunicazione.Add(d);
        }

        // Allegati: solo documenti del fascicolo della qualificazione o della commessa, non riservati.
        var q = await db.Qualifiche.AsNoTracking().Include(x => x.Documenti).FirstAsync(x => x.Id == c.QualificaId, ct);
        var riservati = q.Documenti.Where(d => d.Riservato && d.DocumentoId is not null).Select(d => d.DocumentoId!.Value).ToHashSet();
        var ammessi = (await documenti.ElencoAsync(commessaId, ct: ct)).Select(d => d.Id)
            .Concat(q.Documenti.Where(d => d.DocumentoId is not null).Select(d => d.DocumentoId!.Value)).ToHashSet();
        db.AllegatiComunicazione.RemoveRange(c.Allegati);
        c.Allegati.Clear();
        var ordine = 1;
        foreach (var a in dati.Allegati.OrderBy(a => a.Ordine).DistinctBy(a => a.DocumentoId))
        {
            if (riservati.Contains(a.DocumentoId))
                throw new OperazioneNonValidaException($"«{a.Descrizione}» è segnato come riservato nella qualificazione: non si allega.");
            if (!ammessi.Contains(a.DocumentoId))
                throw new OperazioneNonValidaException($"«{a.Descrizione}» non è tra i documenti della commessa o della qualificazione.");
            var n = new AllegatoComunicazione
            {
                ComunicazioneId = c.Id, CommessaId = commessaId, Ordine = ordine++, DocumentoId = a.DocumentoId,
                Descrizione = Pulisci(a.Descrizione) ?? "Documento allegato",
            };
            c.Allegati.Add(n);
            db.AllegatiComunicazione.Add(n);
        }
        db.NotaStorico = $"Bozza della comunicazione di qualificazione di {q.Fornitore}";
        await db.SaveChangesAsync(ct);

        static void Copia(DestinatarioComunicazione da, DestinatarioComunicazione a)
        {
            a.Ruolo = da.Ruolo;
            a.AttoreId = da.AttoreId;
            a.Selezionato = da.Selezionato;
            a.Titolo = Pulisci(da.Titolo);
            a.Nominativo = Pulisci(da.Nominativo);
            a.Ente = Pulisci(da.Ente);
            a.Indirizzo = Pulisci(da.Indirizzo);
            a.Pec = Pulisci(da.Pec);
            a.Email = Pulisci(da.Email);
        }
    }

    public async Task<VerificaComunicazione> VerificaAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.ComunicazioniQualifica.AsNoTracking().Include(x => x.Destinatari)
            .FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La comunicazione non esiste più.");
        var q = await db.Qualifiche.AsNoTracking().FirstAsync(x => x.Id == c.QualificaId, ct);
        return Verifica(c, q, await impostazioni.LeggiAziendaAsync(ct));
    }

    internal static VerificaComunicazione Verifica(ComunicazioneQualifica c, QualificaFornitore q, DatiAzienda azienda)
    {
        var impedimenti = new List<string>();
        var avvisi = new List<string>();
        if (q.Stato != StatoQualifica.Approvata)
            impedimenti.Add("La qualificazione non è approvata dal Direttore di cantiere: la comunicazione non si può emettere.");
        if (string.IsNullOrWhiteSpace(c.Oggetto)) impedimenti.Add("L'oggetto della comunicazione è obbligatorio.");
        var scelti = c.Destinatari.Where(d => d.Selezionato).ToList();
        if (scelti.Count == 0) impedimenti.Add("Seleziona almeno un destinatario.");
        foreach (var d in scelti)
        {
            var ruolo = Testi.RuoloLettera(d.Ruolo);
            if (string.IsNullOrWhiteSpace(d.Nominativo)) impedimenti.Add($"{ruolo}: manca il nominativo (completalo, o togli la spunta se non è destinatario).");
            else if (string.IsNullOrWhiteSpace(d.Pec) && string.IsNullOrWhiteSpace(d.Email))
                avvisi.Add($"{ruolo} {d.Nominativo}: nessun recapito PEC o email. La lettera riporterà solo il nominativo.");
        }
        foreach (var ruolo in RuoliDestinatari.Where(r => !scelti.Any(d => d.Ruolo == r)))
            avvisi.Add($"{Testi.RuoloLettera(ruolo)}: non è tra i destinatari.");
        if (!azienda.Compilati) avvisi.Add("I dati dell'impresa per l'intestazione non sono compilati (Amministrazione → Dati dell'impresa).");
        return new VerificaComunicazione(impedimenti, avvisi, azienda.Compilati);
    }

    /// <summary>Dati della lettera per l'anteprima (bozza) o la ristampa.</summary>
    public async Task<LetteraQualifica?> LetteraAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        var c = await ComunicazioneAsync(commessaId, id, ct);
        if (c is null) return null;
        var q = await OttieniAsync(commessaId, c.QualificaId, ct);
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        if (q is null || commessa is null) return null;
        var committente = commessa.ClienteId is { } cid ? (await clienti.OttieniAsync(cid, ct))?.Denominazione : null;
        return new LetteraQualifica(await impostazioni.LeggiAziendaAsync(ct), commessa, committente, q, c, await fornitori.OttieniAsync(q.FornitoreId, ct));
    }

    /// <summary>
    /// Genera il PDF definitivo: numero progressivo dell'anno, archiviazione nei documenti della commessa (fascicolo del fornitore),
    /// data e utente della generazione. Da qui il contenuto non cambia più; la generazione non vale come invio.
    /// </summary>
    public async Task<ComunicazioneQualifica> GeneraDefinitivoAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        PuoModificare();
        await BloccoNumeri.WaitAsync(ct);
        try
        {
            await using var db = await fabbrica.CreateDbContextAsync(ct);
            var c = await db.ComunicazioniQualifica.Include(x => x.Destinatari).Include(x => x.Allegati)
                .FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
                ?? throw new OperazioneNonValidaException("La comunicazione non esiste più.");
            if (c.Stato != StatoComunicazione.Bozza) throw new OperazioneNonValidaException("Il PDF definitivo è già stato generato.");
            var q = await db.Qualifiche.AsNoTracking().Include(x => x.Documenti).FirstAsync(x => x.Id == c.QualificaId, ct);
            var azienda = await impostazioni.LeggiAziendaAsync(ct);
            if (Verifica(c, q, azienda) is { Generabile: false } v) throw new OperazioneNonValidaException(v.Impedimenti[0]);

            var anno = c.DataEmissione.Year;
            c.Anno = anno;
            c.Progressivo = (await db.ComunicazioniQualifica.Where(x => x.Anno == anno && x.Progressivo > 0).MaxAsync(x => (int?)x.Progressivo, ct) ?? 0) + 1;
            c.Numero = ComunicazioneQualifica.FormattaNumero(anno, c.Progressivo);
            c.GenerataIl = orologio.GetUtcNow().UtcDateTime;
            c.GenerataDa = utente.Nome;
            c.Allegati.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));

            var commessa = await commesse.OttieniAsync(commessaId, ct) ?? throw new OperazioneNonValidaException("La commessa non esiste più.");
            var committente = commessa.ClienteId is { } cid ? (await clienti.OttieniAsync(cid, ct))?.Denominazione : null;
            q.Documenti.Sort((a, b) => a.Ordine.CompareTo(b.Ordine));
            c.Stato = StatoComunicazione.PdfDefinitivo;
            var pdf = EsportazionePdf.ComunicazioneQualifica(new LetteraQualifica(azienda, commessa, committente, q, c, await fornitori.OttieniAsync(q.FornitoreId, ct)));
            using (var flusso = new MemoryStream(pdf))
            {
                var esito = await documenti.CaricaAsync(flusso, NomeFile(c, q), EsportazioniService.TipoPdf, commessaId, TipoDocumentoComunicazione, ct);
                c.DocumentoId = esito.Documento.Id;
                await documenti.AggiornaMetadatiAsync(esito.Documento.Id, TipoDocumentoComunicazione,
                    $"{c.Numero} – qualificazione di {q.Fornitore} a {DestinatariTesto(c)}", c.DataEmissione, commessaId, ct);
            }
            db.NotaStorico = $"PDF definitivo della comunicazione {c.Numero}";
            db.RegistraOperazione("Comunicazione qualificazione", nameof(ComunicazioneQualifica), c.Id, commessaId,
                $"{c.Numero}: qualificazione di {q.Fornitore} a {DestinatariTesto(c)}", dopo: new { c.Numero, q.Fornitore, c.GenerataDa });
            await db.SaveChangesAsync(ct);
            return c;
        }
        finally
        {
            BloccoNumeri.Release();
        }
    }

    /// <summary>Approvazione per l'invio: riservata all'amministratore e agli operatori abilitati a emettere ordini.</summary>
    public async Task ApprovaInvioAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        if (!UtenteCorrente.PuoEmettereOrdini)
            throw new OperazioneNonValidaException("L'approvazione per l'invio è riservata all'amministratore e agli operatori abilitati a emettere ordini.");
        await CambiaStatoAsync(commessaId, id, StatoComunicazione.PdfDefinitivo, c =>
        {
            c.Stato = StatoComunicazione.ApprovataPerInvio;
            c.ApprovataIl = orologio.GetUtcNow().UtcDateTime;
            c.ApprovataDa = utente.Nome;
            return $"Comunicazione {c.Numero} approvata per l'invio";
        }, ct);
    }

    /// <summary>Registra la trasmissione effettuata dall'operatore con il canale aziendale (Corico non invia).</summary>
    public async Task RegistraInvioAsync(Guid commessaId, Guid id, DateOnly inviataIl, ModalitaInvio modalita, string? destinatariEffettivi,
        string? riferimento, CancellationToken ct = default)
    {
        PuoModificare();
        await CambiaStatoAsync(commessaId, id, StatoComunicazione.ApprovataPerInvio, c =>
        {
            if (inviataIl < c.DataEmissione) throw new OperazioneNonValidaException("La data di trasmissione non può precedere la data di emissione.");
            c.Stato = StatoComunicazione.Inviata;
            c.InviataIl = inviataIl;
            c.Modalita = modalita;
            c.DestinatariEffettivi = Pulisci(destinatariEffettivi) ?? DestinatariTesto(c);
            c.RiferimentoInvio = Pulisci(riferimento);
            c.InvioRegistratoDa = utente.Nome;
            return $"Invio della comunicazione {c.Numero} registrato";
        }, ct);
    }

    /// <summary>Ricevuta di consegna o conferma acquisita, con il file dall'archivio se disponibile.</summary>
    public async Task RegistraRicevutaAsync(Guid commessaId, Guid id, DateOnly ricevutaIl, string? riferimento, Guid? documentoId,
        CancellationToken ct = default)
    {
        PuoModificare();
        await CambiaStatoAsync(commessaId, id, StatoComunicazione.Inviata, c =>
        {
            if (c.InviataIl is { } inv && ricevutaIl < inv) throw new OperazioneNonValidaException("La ricevuta non può precedere la trasmissione.");
            c.Stato = StatoComunicazione.RicevutaAcquisita;
            c.RicevutaIl = ricevutaIl;
            c.RiferimentoRicevuta = Pulisci(riferimento);
            c.RicevutaDocumentoId = documentoId;
            return $"Ricevuta della comunicazione {c.Numero} acquisita";
        }, ct);
    }

    /// <summary>Annulla una comunicazione generata (es. per un errore): il PDF resta in archivio; se ne prepara una nuova.</summary>
    public async Task AnnullaAsync(Guid commessaId, Guid id, string motivo, CancellationToken ct = default)
    {
        PuoModificare();
        if (string.IsNullOrWhiteSpace(motivo)) throw new OperazioneNonValidaException("Il motivo dell'annullamento è obbligatorio.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.ComunicazioniQualifica.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La comunicazione non esiste più.");
        if (c.Stato == StatoComunicazione.Bozza) throw new OperazioneNonValidaException("La bozza non è ancora generata: puoi eliminarla.");
        if (c.Stato == StatoComunicazione.Annullata) throw new OperazioneNonValidaException("La comunicazione è già annullata.");
        c.Stato = StatoComunicazione.Annullata;
        c.AnnullataIl = Oggi();
        c.MotivoAnnullamento = motivo.Trim();
        db.NotaStorico = $"Annullamento della comunicazione {c.Numero}: {c.MotivoAnnullamento}";
        await db.SaveChangesAsync(ct);
    }

    public async Task EliminaBozzaAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        PuoModificare();
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.ComunicazioniQualifica.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La comunicazione non esiste più.");
        if (c.Stato != StatoComunicazione.Bozza) throw new OperazioneNonValidaException("Si eliminano solo le bozze: annulla la comunicazione generata.");
        db.ComunicazioniQualifica.Remove(c);
        db.NotaStorico = "Eliminata una bozza di comunicazione di qualificazione";
        await db.SaveChangesAsync(ct);
    }

    private async Task CambiaStatoAsync(Guid commessaId, Guid id, StatoComunicazione atteso, Func<ComunicazioneQualifica, string> cambia, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.ComunicazioniQualifica.Include(x => x.Destinatari).FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La comunicazione non esiste più.");
        if (c.Stato != atteso)
            throw new OperazioneNonValidaException($"Operazione possibile solo con la comunicazione nello stato «{Testi.StatoComunicazione(atteso)}» " +
                                                   $"(ora è «{Testi.StatoComunicazione(c.Stato)}»).");
        db.NotaStorico = cambia(c);
        await db.SaveChangesAsync(ct);
    }

    // ---- Supporto ----

    internal static ComunicazioneQualifica? UltimaDi(IEnumerable<ComunicazioneQualifica> c, Guid qualificaId) =>
        c.Where(x => x.QualificaId == qualificaId).OrderBy(x => x.Stato == StatoComunicazione.Annullata ? 1 : 0).ThenByDescending(x => x.CreatoIl).FirstOrDefault();

    internal static string DestinatariTesto(ComunicazioneQualifica c) =>
        string.Join(", ", c.Destinatari.Where(d => d.Selezionato).OrderBy(d => Array.IndexOf(RuoliDestinatari, d.Ruolo))
            .Select(d => RuoliCommessa.Sigla(d.Ruolo)).Distinct()) is { Length: > 0 } s ? s : "—";

    internal static string Nominativo(string? titolo, string? nome) =>
        string.Join(" ", new[] { titolo, nome }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

    private static string NomeFile(ComunicazioneQualifica c, QualificaFornitore q)
    {
        var fornitore = new string(q.Fornitore.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        return $"{c.Numero.Replace('/', '-')} Comunicazione qualificazione {fornitore}.pdf";
    }

    private void PuoModificare()
    {
        if (utente.Identita?.Ruolo == RuoloUtente.Consultazione)
            throw new OperazioneNonValidaException("Con il profilo di consultazione non puoi modificare le qualificazioni.");
    }

    private DateOnly Oggi() => DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
