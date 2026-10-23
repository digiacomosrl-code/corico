using Corico.Core.Dominio;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Riga della struttura WBS con le voci del computo attivo e il ricavo, compresi i sottolivelli.</summary>
public sealed record RigaWbs(Guid Id, int Versione, string Codice, string Descrizione, int Livello, int VociDirette, int Voci,
    decimal RicavoNetto);

/// <summary>Struttura WBS letta da un file Excel, prima della conferma.</summary>
public sealed record AnteprimaWbs(IReadOnlyList<ElementoWbsDati> Elementi, IReadOnlyList<string> Segnalazioni);

/// <summary>
/// WBS della commessa: struttura (codice e descrizione, gerarchia per punti), assegnazione alle voci del computo,
/// caricamento da Excel, da un'altra commessa o dal computo PriMus, proposta automatica con conferma.
/// La WBS non è un prezzo contrattuale: si modifica anche dopo l'approvazione del budget.
/// </summary>
public sealed class WbsService(IDbContextFactory<PreventivoDbContext> fabbrica, IPropostaWbs proposta)
{
    public string NomeProposta => proposta.Nome;

    public async Task<IReadOnlyList<RigaWbs>> ElencoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var elementi = await db.ElementiWbs.AsNoTracking().Where(e => e.CommessaId == commessaId).ToListAsync(ct);
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        var voci = (await VociAttiveAsync(db, commessaId, ct)).Where(v => v.CodiceWbs is not null)
            .Select(v => (v.CodiceWbs, CalcoloRicavi.Voce(v.Quantita, v.Prezzo, preventivo.RibassoPercentuale, v.PercentualeNonSoggetta).Netto))
            .ToList();
        return elementi.OrderBy(e => e.Codice, CodiciWbs.Ordine)
            .Select(e =>
            {
                var sotto = voci.Where(v => CodiciWbs.Rientra(v.CodiceWbs, e.Codice)).ToList();
                return new RigaWbs(e.Id, e.Versione, e.Codice, e.Descrizione, CodiciWbs.Livello(e.Codice),
                    voci.Count(v => v.CodiceWbs == e.Codice), sotto.Count, sotto.Sum(v => v.Netto));
            })
            .ToList();
    }

    /// <summary>Codice → descrizione, per mostrare la descrizione accanto ai codici.</summary>
    public async Task<IReadOnlyDictionary<string, string>> DescrizioniAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.ElementiWbs.AsNoTracking().Where(e => e.CommessaId == commessaId)
            .ToDictionaryAsync(e => e.Codice, e => e.Descrizione, StringComparer.OrdinalIgnoreCase, ct);
    }

    /// <summary>Commesse che hanno una WBS, da cui copiarla.</summary>
    public async Task<IReadOnlyList<Guid>> CommesseConWbsAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.ElementiWbs.AsNoTracking().Select(e => e.CommessaId).Distinct().ToListAsync(ct);
    }

    /// <summary>
    /// Nuovo elemento (id vuoto) o modifica. Se cambia il codice, cambiano allo stesso modo i sottolivelli e le voci assegnate
    /// (anche quelle dei computi precedenti); gli ordini già emessi conservano il codice di allora.
    /// </summary>
    public async Task SalvaAsync(Guid commessaId, Guid id, int versioneLetta, string? codice, string? descrizione, CancellationToken ct = default)
    {
        var c = CodiciWbs.Normalizza(codice) ?? throw new OperazioneNonValidaException("Indica il codice WBS (es. 2.3).");
        if (c.Length > CodiciWbs.LunghezzaMassima) throw new OperazioneNonValidaException("Il codice WBS è troppo lungo.");
        if (string.IsNullOrWhiteSpace(descrizione)) throw new OperazioneNonValidaException("Indica la descrizione della WBS.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var elementi = await db.ElementiWbs.Where(e => e.CommessaId == commessaId).ToListAsync(ct);
        if (elementi.Any(e => e.Id != id && string.Equals(e.Codice, c, StringComparison.OrdinalIgnoreCase)))
            throw new OperazioneNonValidaException($"Il codice WBS {c} esiste già.");

        if (id == Guid.Empty)
        {
            db.ElementiWbs.Add(new ElementoWbs { CommessaId = commessaId, Codice = c, Descrizione = descrizione.Trim() });
            db.NotaStorico = $"Nuova WBS {c}";
        }
        else
        {
            var e = elementi.FirstOrDefault(x => x.Id == id) ?? throw new OperazioneNonValidaException("La WBS non esiste più.");
            db.Entry(e).Property(x => x.Versione).OriginalValue = versioneLetta;
            var vecchio = e.Codice;
            e.Descrizione = descrizione.Trim();
            if (!string.Equals(vecchio, c, StringComparison.Ordinal))
            {
                string Nuovo(string codiceAttuale) => c + codiceAttuale[vecchio.Length..];
                var sotto = elementi.Where(x => x.Id != id && CodiciWbs.Rientra(x.Codice, vecchio)).ToList();
                var occupati = elementi.Except(sotto).Where(x => x.Id != id).Select(x => x.Codice).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (sotto.Select(x => Nuovo(x.Codice)).FirstOrDefault(occupati.Contains) is { } conflitto)
                    throw new OperazioneNonValidaException($"Cambiando il codice, il sottolivello diventerebbe {conflitto}, che esiste già.");
                e.Codice = c;
                foreach (var s in sotto) s.Codice = Nuovo(s.Codice);
                var voci = await db.VociComputo.Where(v => v.CommessaId == commessaId && v.CodiceWbs != null).ToListAsync(ct);
                foreach (var v in voci.Where(v => CodiciWbs.Rientra(v.CodiceWbs, vecchio))) v.CodiceWbs = Nuovo(v.CodiceWbs!);
                db.NotaStorico = $"WBS {vecchio} rinominata in {c}";
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Elimina un elemento senza sottolivelli e senza voci assegnate nel computo attivo.</summary>
    public async Task EliminaAsync(Guid commessaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var e = await db.ElementiWbs.FirstOrDefaultAsync(x => x.Id == id && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("La WBS non esiste più.");
        var codici = await db.ElementiWbs.Where(x => x.CommessaId == commessaId).Select(x => x.Codice).ToListAsync(ct);
        if (codici.Any(x => x != e.Codice && CodiciWbs.Rientra(x, e.Codice)))
            throw new OperazioneNonValidaException($"La WBS {e.Codice} ha dei sottolivelli: eliminali prima.");
        var usate = (await VociAttiveAsync(db, commessaId, ct)).Count(v => v.CodiceWbs == e.Codice);
        if (usate > 0)
            throw new OperazioneNonValidaException($"La WBS {e.Codice} è assegnata a {usate} voci del computo: togli prima l'assegnazione.");
        db.ElementiWbs.Remove(e);
        db.NotaStorico = $"WBS {e.Codice} eliminata";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Assegna il codice alle voci indicate (null toglie la WBS). Il codice deve esistere nella struttura.</summary>
    public async Task<int> AssegnaAsync(Guid commessaId, IReadOnlyCollection<Guid> voci, string? codice, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        string? c = null;
        if (!string.IsNullOrWhiteSpace(codice))
        {
            var n = CodiciWbs.Normalizza(codice);
            var codici = await db.ElementiWbs.Where(e => e.CommessaId == commessaId).Select(e => e.Codice).ToListAsync(ct);
            c = codici.FirstOrDefault(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            if (c is null) throw new OperazioneNonValidaException($"La WBS {codice} non è nella struttura della commessa: aggiungila prima.");
        }
        var elenco = await db.VociComputo.Where(v => v.CommessaId == commessaId && voci.Contains(v.Id)).ToListAsync(ct);
        foreach (var v in elenco) v.CodiceWbs = c;
        db.NotaStorico = c is null ? $"WBS tolta da {elenco.Count} voci" : $"WBS {c} assegnata a {elenco.Count} voci";
        await db.SaveChangesAsync(ct);
        return elenco.Count;
    }

    // ---- Caricamento della struttura ----

    /// <summary>
    /// Legge la WBS da Excel: il foglio «WBS» se c'è, altrimenti il primo. In ogni riga il primo valore con forma di codice
    /// (1, 2.3, 6.2.1) e il testo che lo segue sono codice e descrizione; le altre righe si ignorano.
    /// </summary>
    public static AnteprimaWbs AnteprimaExcel(Stream contenuto)
    {
        var fogli = LettoreXlsx.Leggi(contenuto);
        var f = fogli.FirstOrDefault(x => x.Nome.Trim().Equals("WBS", StringComparison.OrdinalIgnoreCase))
                ?? fogli.FirstOrDefault(x => x.Nome.Contains("WBS", StringComparison.OrdinalIgnoreCase))
                ?? fogli.FirstOrDefault() ?? throw new FormatException("Il file non contiene fogli.");
        var elementi = new List<ElementoWbsDati>();
        var segnalazioni = new List<string>();
        for (var r = 1; r <= f.UltimaRiga; r++)
        {
            for (var col = 1; col < f.UltimaColonna; col++)
            {
                var codice = CodiciWbs.Normalizza(f.Testo(r, col));
                if (codice is null || !FormaDiCodice(codice)) continue;
                var descrizione = Enumerable.Range(col + 1, f.UltimaColonna - col).Select(c => f.Testo(r, c))
                    .FirstOrDefault(t => t is not null && !FormaDiCodice(t));
                if (descrizione is null) break;
                if (elementi.FirstOrDefault(e => e.Codice == codice) is { } doppio)
                    segnalazioni.Add($"Riga {r}: il codice {codice} è già usato da «{doppio.Descrizione}»; «{descrizione}» non viene caricato. Correggi il file o aggiungilo a mano con un altro codice.");
                else
                    elementi.Add(new(codice, descrizione.Trim()));
                break;
            }
        }
        if (elementi.Count == 0) throw new FormatException($"Nel foglio «{f.Nome}» non ho trovato righe con codice WBS e descrizione.");
        segnalazioni.Insert(0, $"Foglio «{f.Nome}»: {elementi.Count} elementi WBS.");
        return new AnteprimaWbs(elementi, segnalazioni);
    }

    private static bool FormaDiCodice(string t) =>
        t.Length <= CodiciWbs.LunghezzaMassima && t.Split('.').All(p => p.Length is > 0 and <= 4 && p.All(char.IsLetterOrDigit))
        && char.IsDigit(t[0]) && t.Count(char.IsLetter) <= 1;

    /// <summary>Aggiunge alla struttura gli elementi mancanti; quelli con lo stesso codice restano come sono.</summary>
    public async Task<(int Aggiunti, int GiaPresenti)> CaricaAsync(Guid commessaId, IReadOnlyList<ElementoWbsDati> elementi, string origine,
        CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var aggiunti = await AggiungiMancantiAsync(db, commessaId, elementi, ct);
        db.InserimentiSoloInRiepilogo = true;
        db.RegistraOperazione("Caricamento WBS", nameof(ElementoWbs), null, commessaId, $"WBS da {origine}: {aggiunti} elementi aggiunti");
        await db.SaveChangesAsync(ct);
        return (aggiunti, elementi.Count - aggiunti);
    }

    public async Task<(int Aggiunti, int GiaPresenti)> CopiaDaCommessaAsync(Guid commessaId, Guid origineId, string nomeOrigine,
        CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var elementi = await db.ElementiWbs.AsNoTracking().Where(e => e.CommessaId == origineId)
            .Select(e => new ElementoWbsDati(e.Codice, e.Descrizione)).ToListAsync(ct);
        if (elementi.Count == 0) throw new OperazioneNonValidaException("La commessa scelta non ha una WBS.");
        return await CaricaAsync(commessaId, elementi, $"commessa {nomeOrigine}", ct);
    }

    /// <summary>Aggiunge (senza salvare) gli elementi con codice nuovo; restituisce quanti.</summary>
    internal static async Task<int> AggiungiMancantiAsync(PreventivoDbContext db, Guid commessaId, IEnumerable<ElementoWbsDati> elementi,
        CancellationToken ct)
    {
        var esistenti = (await db.ElementiWbs.Where(e => e.CommessaId == commessaId).Select(e => e.Codice).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        esistenti.UnionWith(db.ElementiWbs.Local.Where(e => e.CommessaId == commessaId).Select(e => e.Codice));
        var aggiunti = 0;
        foreach (var e in elementi)
        {
            var c = CodiciWbs.Normalizza(e.Codice);
            if (c is null || c.Length > CodiciWbs.LunghezzaMassima || !esistenti.Add(c)) continue;
            var d = e.Descrizione.Trim();
            db.ElementiWbs.Add(new ElementoWbs { CommessaId = commessaId, Codice = c, Descrizione = d.Length > 300 ? d[..300] : d });
            aggiunti++;
        }
        return aggiunti;
    }

    // ---- Proposta automatica ----

    public async Task<PropostaWbs> ProponiAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var wbs = await db.ElementiWbs.AsNoTracking().Where(e => e.CommessaId == commessaId)
            .Select(e => new ElementoWbsDati(e.Codice, e.Descrizione)).ToListAsync(ct);
        var categorie = await db.Categorie.AsNoTracking().Where(c => c.CommessaId == commessaId).ToDictionaryAsync(c => c.Id, c => c.Descrizione, ct);
        var voci = (await VociAttiveAsync(db, commessaId, ct)).OrderBy(v => v.Ordine)
            .Select(v => new VocePerWbs(v.Id, v.Ordine, v.Tariffa, v.Descrizione,
                v.SuperCategoriaId is { } sc ? categorie.GetValueOrDefault(sc) : null,
                v.CategoriaId is { } c ? categorie.GetValueOrDefault(c) : null,
                v.CodiceWbs, v.Quantita * v.Prezzo))
            .ToList();
        if (voci.Count == 0) throw new OperazioneNonValidaException("Non c'è un computo con voci: importa prima il computo.");
        return await proposta.ProponiAsync(new ContestoPropostaWbs(commessaId, wbs, voci), ct);
    }

    /// <summary>Applica la parte confermata della proposta: nuovi elementi e assegnazioni (solo alle voci ancora senza WBS).</summary>
    public async Task<int> ApplicaPropostaAsync(Guid commessaId, IReadOnlyList<ElementoWbsDati> nuovi,
        IReadOnlyList<AssegnazioneWbsProposta> assegnazioni, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var aggiunti = await AggiungiMancantiAsync(db, commessaId, nuovi, ct);
        var codici = db.ElementiWbs.Local.Where(e => e.CommessaId == commessaId).Select(e => e.Codice)
            .Concat(await db.ElementiWbs.Where(e => e.CommessaId == commessaId).Select(e => e.Codice).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var perVoce = assegnazioni.Where(a => codici.Contains(a.Codice)).GroupBy(a => a.VoceId).ToDictionary(g => g.Key, g => g.First().Codice);
        var ids = perVoce.Keys.ToList();
        var voci = await db.VociComputo.Where(v => v.CommessaId == commessaId && ids.Contains(v.Id) && v.CodiceWbs == null).ToListAsync(ct);
        foreach (var v in voci) v.CodiceWbs = perVoce[v.Id];
        db.InserimentiSoloInRiepilogo = true;
        db.RegistraOperazione("Proposta WBS", nameof(ElementoWbs), null, commessaId,
            $"Proposta WBS confermata: {aggiunti} elementi aggiunti, {voci.Count} voci assegnate");
        await db.SaveChangesAsync(ct);
        return voci.Count;
    }

    private static async Task<List<VoceComputo>> VociAttiveAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var computo = await db.Computi.AsNoTracking().Where(c => c.CommessaId == commessaId && c.Attivo).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        return computo is null ? [] : await db.VociComputo.AsNoTracking().Where(v => v.ComputoId == computo && !v.Archiviata).ToListAsync(ct);
    }
}
