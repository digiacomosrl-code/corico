using System.Text.Json;
using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Controllo eseguito prima di proporre un budget: i bloccanti non superati impediscono la proposta.</summary>
public sealed record ControlloBudget(string Descrizione, bool Superato, bool Bloccante, IReadOnlyList<string> Dettagli)
{
    public bool Blocca => Bloccante && !Superato;
}

public sealed record StatoBudgetCommessa(
    IReadOnlyList<VersionePreventivo> Budget,
    IReadOnlyList<VersionePreventivo> VersioniStoriche,
    VersionePreventivo? InApprovazione,
    VersionePreventivo? Approvato,
    int ProssimoNumero,
    string? MotivoNonProponibile,
    IReadOnlyList<ControlloBudget> Controlli)
{
    /// <summary>Budget di riferimento del tabellone: l'approvato in vigore, altrimenti quello in approvazione.</summary>
    public VersionePreventivo? Riferimento => Approvato ?? InApprovazione;

    public bool Proponibile => MotivoNonProponibile is null && !Controlli.Any(c => c.Blocca);
}

/// <summary>
/// Budget della commessa (Modulo 2, rilascio 2A): quando computo, pacchetti e Lista della Spesa sono completi si propone il
/// Budget 0, fotografia immutabile di costi e ricavi preventivi; l'amministratore lo approva o lo respinge con un motivo.
/// Dopo l'approvazione i prezzi contrattuali sono bloccati e tutto si misura come scostamento dal budget.
/// Un nuovo budget (1, 2…) nasce solo da una perizia di variante (rilascio 2C).
/// </summary>
public sealed class BudgetService(
    IDbContextFactory<PreventivoDbContext> fabbrica, RiepilogoService riepilogo, CommesseService commesse,
    IUtenteCorrente utente, TimeProvider orologio)
{
    public async Task<StatoBudgetCommessa> StatoAsync(Guid commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await StatoAsync(db, commessaId, ct);
    }

    private async Task<StatoBudgetCommessa> StatoAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var versioni = await db.Versioni.AsNoTracking().Where(v => v.CommessaId == commessaId)
            .OrderByDescending(v => v.Numero).ToListAsync(ct);
        var budget = versioni.Where(v => v.EBudget).ToList();
        var inApprovazione = budget.FirstOrDefault(v => v.StatoBudget == StatoBudget.InApprovazione);
        var approvato = Approvato(budget);

        string? motivo = null;
        if (inApprovazione is not null)
            motivo = $"Il Budget {inApprovazione.NumeroBudget} è in approvazione: l'amministratore deve approvarlo o respingerlo.";
        else if (approvato is not null)
            motivo = $"Il Budget {approvato.NumeroBudget} è approvato: un nuovo budget nasce solo da una perizia di variante.";

        var controlli = motivo is null ? await ControlliAsync(db, commessaId, ct) : [];
        return new StatoBudgetCommessa(budget, versioni.Where(v => !v.EBudget).ToList(), inApprovazione, approvato,
            approvato is null ? 0 : approvato.NumeroBudget!.Value + 1, motivo, controlli);
    }

    /// <summary>Ultimo budget approvato (quello in vigore).</summary>
    public static VersionePreventivo? Approvato(IEnumerable<VersionePreventivo> versioni) =>
        versioni.Where(v => v.StatoBudget == StatoBudget.Approvato).MaxBy(v => v.NumeroBudget);

    /// <summary>Budget di riferimento: l'approvato in vigore, altrimenti quello in approvazione.</summary>
    internal static async Task<(VersionePreventivo Versione, Fotografia Fotografia)?> RiferimentoAsync(
        PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var budget = await db.Versioni.AsNoTracking()
            .Where(v => v.CommessaId == commessaId && v.NumeroBudget != null && v.StatoBudget != StatoBudget.Respinto).ToListAsync(ct);
        var v = Approvato(budget) ?? budget.Where(x => x.StatoBudget == StatoBudget.InApprovazione).MaxBy(x => x.Numero);
        return v is null ? null : (v, VersioniService.Leggi(v));
    }

    public static string Etichetta(VersionePreventivo v) => v.StatoBudget switch
    {
        StatoBudget.Approvato => $"Budget {v.NumeroBudget} approvato",
        StatoBudget.Respinto => $"Budget {v.NumeroBudget} respinto",
        StatoBudget.InApprovazione => $"Budget {v.NumeroBudget} in approvazione",
        _ => $"Versione storica n. {v.Numero}",
    };

    /// <summary>Controlli prima della proposta (specifica Modulo 2, «Controlli prima della proposta»).</summary>
    private async Task<IReadOnlyList<ControlloBudget>> ControlliAsync(PreventivoDbContext db, Guid commessaId, CancellationToken ct)
    {
        var f = await riepilogo.FotografiaAsync(db, commessaId, ct);
        var lista = await ListaSpesaService.CalcolaAsync(db, commessaId, ct);
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        var oggi = DateOnly.FromDateTime(orologio.GetLocalNow().DateTime);
        var controlli = new List<ControlloBudget>();

        var libere = f.Voci.Where(v => v.PacchettoId is null).ToList();
        controlli.Add(new("Tutte le voci del computo assegnate a un pacchetto", f.Voci.Count > 0 && libere.Count == 0, true,
            f.Voci.Count == 0 ? ["Il computo della commessa è vuoto."]
            : Elenco(libere.Select(v => $"{v.Tariffa} – {v.Descrizione}".Trim(' ', '–')), "voci")));

        var senzaOfferta = f.Pacchetti.Where(p => p.Costo is null).ToList();
        controlli.Add(new("Ogni pacchetto con un'offerta scelta o una stima interna", senzaOfferta.Count == 0, true,
            Elenco(senzaOfferta.Select(p => p.Descrizione), "pacchetti")));

        var pianificato = f.Pacchetti.Sum(p => p.CostoPianificato ?? 0);
        var concorda = Math.Abs(Importi.Arrotonda(lista.Totale) - Importi.Arrotonda(pianificato)) < 0.01m;
        controlli.Add(new("Totale della Lista della Spesa uguale al costo dei pacchetti", concorda, true,
            concorda ? [] : [$"Lista della Spesa {Formato.Euro(lista.Totale)}, costo dei pacchetti {Formato.Euro(pianificato)}: è un errore da segnalare."]));

        var datiMancanti = new List<string>();
        if (commessa?.ClienteId is null) datiMancanti.Add("Indica il cliente nei Dati generali della commessa.");
        controlli.Add(new("Dati della commessa obbligatori (cliente)", datiMancanti.Count == 0, true, datiMancanti));

        var avvisi = new List<string>();
        if (f.RibassoPercentuale == 0) avvisi.Add("Il ribasso d'asta è 0%: verifica che sia corretto.");
        foreach (var p in f.Pacchetti.Where(p => p.ScadenzaOfferta is { } s && s < oggi))
            avvisi.Add($"L'offerta scelta per «{p.Descrizione}» ({p.Fornitore}) è scaduta il {Formato.Data(p.ScadenzaOfferta)}.");
        if (f.ObiettivoMarginePercentuale is { } obiettivo && f.Totali.MarginePercentuale is { } m && m < obiettivo)
            avvisi.Add($"Il margine di commessa ({Formato.Percento(m)}) è sotto l'obiettivo del {Formato.Percento(obiettivo)}.");
        controlli.Add(new("Ribasso, offerte scadute e margine rispetto all'obiettivo", avvisi.Count == 0, false, avvisi));
        return controlli;
    }

    private static IReadOnlyList<string> Elenco(IEnumerable<string> elementi, string nome)
    {
        var tutti = elementi.ToList();
        return tutti.Count <= 15 ? tutti : [.. tutti.Take(15), $"… e altri {tutti.Count - 15} {nome}"];
    }

    /// <summary>
    /// Propone il budget: verifica i controlli e salva la fotografia della bozza in stato «In approvazione», non modificabile.
    /// Gli avvisi non bloccanti restano nella nota del budget.
    /// </summary>
    public async Task<VersionePreventivo> ProponiAsync(Guid commessaId, string? nota, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var stato = await StatoAsync(db, commessaId, ct);
        if (stato.MotivoNonProponibile is { } motivo) throw new OperazioneNonValidaException(motivo);
        if (stato.Controlli.FirstOrDefault(c => c.Blocca) is { } bloccante)
            throw new OperazioneNonValidaException($"Controllo non superato: {bloccante.Descrizione.ToLowerInvariant()}.");

        var f = await riepilogo.FotografiaAsync(db, commessaId, ct);
        var numero = (await db.Versioni.Where(v => v.CommessaId == commessaId).MaxAsync(v => (int?)v.Numero, ct) ?? 0) + 1;
        var avvisi = stato.Controlli.Where(c => !c.Bloccante).SelectMany(c => c.Dettagli).ToList();
        var v = new VersionePreventivo
        {
            CommessaId = commessaId,
            Numero = numero,
            NumeroBudget = stato.ProssimoNumero,
            StatoBudget = StatoBudget.InApprovazione,
            Nota = string.IsNullOrWhiteSpace(nota) ? $"Budget {stato.ProssimoNumero}" : nota.Trim(),
            Avvisi = avvisi.Count == 0 ? null : string.Join("\n", avvisi),
            Fotografia = JsonSerializer.Serialize(f, VersioniService.OpzioniJson),
            RibassoPercentuale = f.RibassoPercentuale,
            Ricavo = f.Totali.Ricavo,
            Costo = f.Totali.Costo,
            Mdc = f.Totali.Mdc,
            SpeseGenerali = f.Totali.SpeseGenerali,
            Risultato = f.Totali.Risultato,
        };
        db.Versioni.Add(v);
        db.InserimentiSoloInRiepilogo = true;
        db.RegistraOperazione("Proposta budget", nameof(VersionePreventivo), v.Id, commessaId,
            $"Budget {v.NumeroBudget} proposto per l'approvazione: {v.Nota}",
            dopo: new { v.NumeroBudget, Ricavo = Importi.Arrotonda(v.Ricavo), Costo = Importi.Arrotonda(v.Costo), Risultato = Importi.Arrotonda(v.Risultato) });
        await db.SaveChangesAsync(ct);
        return v;
    }

    /// <summary>Approvazione dell'amministratore: da qui il budget è immutabile e i prezzi contrattuali sono bloccati.</summary>
    public Task ApprovaAsync(Guid commessaId, Guid versioneId, CancellationToken ct = default) =>
        EsitoAsync(commessaId, versioneId, StatoBudget.Approvato, null, ct);

    /// <summary>Rifiuto con motivo: la fotografia resta nello storico e si torna a lavorare sulla bozza.</summary>
    public Task RespingiAsync(Guid commessaId, Guid versioneId, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new OperazioneNonValidaException("Il motivo del rifiuto è obbligatorio.");
        return EsitoAsync(commessaId, versioneId, StatoBudget.Respinto, motivo.Trim(), ct);
    }

    private async Task EsitoAsync(Guid commessaId, Guid versioneId, StatoBudget esito, string? motivo, CancellationToken ct)
    {
        if (!UtenteCorrente.PuoAmministrare)
            throw new OperazioneNonValidaException("L'approvazione del budget è riservata all'amministratore.");
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var v = await db.Versioni.FirstOrDefaultAsync(x => x.Id == versioneId && x.CommessaId == commessaId, ct)
            ?? throw new OperazioneNonValidaException("Il budget indicato non esiste.");
        if (v.StatoBudget != StatoBudget.InApprovazione)
            throw new OperazioneNonValidaException("Il budget non è in approvazione.");
        v.StatoBudget = esito;
        v.EsitoDa = utente.Nome;
        v.EsitoIl = orologio.GetUtcNow().UtcDateTime;
        v.MotivoRifiuto = motivo;
        db.NotaStorico = esito == StatoBudget.Approvato
            ? $"Budget {v.NumeroBudget} approvato"
            : $"Budget {v.NumeroBudget} respinto: {motivo}";
        await db.SaveChangesAsync(ct);
    }
}
