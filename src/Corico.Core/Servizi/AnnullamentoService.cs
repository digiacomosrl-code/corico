using System.Text.Json;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

/// <summary>Campo di una modifica da annullare: valore attuale, valore da ripristinare e se è cambiato dopo la modifica.</summary>
public sealed record CampoAnnullamento(string Campo, JsonElement Attuale, JsonElement DaRipristinare, bool CambiatoDopo);

public sealed record AnteprimaAnnullamento(
    long VoceId, bool Possibile, string? Motivo, IReadOnlyList<CampoAnnullamento> Campi, int ModificheSuccessive)
{
    /// <summary>Qualche campo è stato cambiato di nuovo dopo la modifica: l'annullamento sovrascrive anche quelle modifiche.</summary>
    public bool RichiedeConfermaAggiuntiva => Campi.Any(c => c.CambiatoDopo);
}

/// <summary>
/// Annullamento selettivo delle modifiche registrate nello storico (sez. 9).
/// È ammesso solo dove è tecnicamente sicuro: modifiche di campi di un elemento che esiste ancora.
/// Inserimenti, eliminazioni, operazioni riepilogative (importazioni) ed elementi immutabili non si annullano da qui.
/// L'annullamento è a sua volta una modifica registrata, quindi si può annullare.
/// </summary>
public sealed class AnnullamentoService(IDbContextFactory<CoreDbContext> fabbricaCore, IEnumerable<IContestoAnnullabile> contesti)
{
    public static bool Annullabile(VoceStorico voce) =>
        voce.Operazione == "Modifica" && voce.EntitaId is not null && !string.IsNullOrEmpty(voce.ValoriPrecedenti);

    public async Task<AnteprimaAnnullamento> AnteprimaAsync(long voceId, CancellationToken ct = default)
    {
        var (anteprima, db) = await PreparaAsync(voceId, ct);
        if (db is not null) await db.DisposeAsync();
        return anteprima;
    }

    /// <summary>
    /// Ripristina i valori precedenti alla modifica. Se nel frattempo i campi sono cambiati ancora,
    /// serve <paramref name="confermaSovrascrittura"/>.
    /// </summary>
    public async Task AnnullaAsync(long voceId, bool confermaSovrascrittura, CancellationToken ct = default)
    {
        var (anteprima, db) = await PreparaAsync(voceId, ct, applica: true);
        if (db is null || !anteprima.Possibile)
            throw new OperazioneNonValidaException(anteprima.Motivo ?? "Questa modifica non si può annullare.");
        await using (db)
        {
            if (anteprima.RichiedeConfermaAggiuntiva && !confermaSovrascrittura)
                throw new OperazioneNonValidaException(
                    "Alcuni campi sono stati modificati di nuovo dopo questa operazione: conferma per sovrascriverli.");
            var voce = await db.Storico.AsNoTracking().SingleAsync(v => v.Id == voceId, ct);
            db.NotaStorico = $"Annullamento della modifica del {voce.DataOra.ToLocalTime():dd/MM/yyyy HH:mm} (n. {voceId})";
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                throw new OperazioneNonValidaException(
                    "Non è possibile ripristinare i valori precedenti: un elemento collegato non esiste più.");
            }
        }
    }

    private async Task<(AnteprimaAnnullamento, CoricoDbContextBase?)> PreparaAsync(long voceId, CancellationToken ct, bool applica = false)
    {
        AnteprimaAnnullamento NonPossibile(string motivo) => new(voceId, false, motivo, [], 0);

        VoceStorico? voce;
        int successive;
        await using (var core = await fabbricaCore.CreateDbContextAsync(ct))
        {
            voce = await core.Storico.AsNoTracking().SingleOrDefaultAsync(v => v.Id == voceId, ct);
            if (voce is null) return (NonPossibile("Operazione non trovata nello storico."), null);
            successive = await core.Storico.CountAsync(v => v.Id > voceId && v.Entita == voce.Entita && v.EntitaId == voce.EntitaId, ct);
        }
        if (!Annullabile(voce))
            return (NonPossibile("Si possono annullare solo le modifiche di dati; inserimenti, eliminazioni e importazioni no."), null);

        foreach (var contesto in contesti)
        {
            var db = await contesto.CreaAsync(ct);
            var tipo = db.Model.GetEntityTypes().FirstOrDefault(t => t.ClrType.Name == voce.Entita && !t.IsOwned());
            if (tipo is null) { await db.DisposeAsync(); continue; }

            if (typeof(IImmutabile).IsAssignableFrom(tipo.ClrType))
            {
                await db.DisposeAsync();
                return (NonPossibile("L'elemento non è modificabile (es. versione ufficiale)."), null);
            }
            var entita = await db.FindAsync(tipo.ClrType, [voce.EntitaId!.Value], ct);
            if (entita is null)
            {
                await db.DisposeAsync();
                return (NonPossibile("L'elemento è stato eliminato dopo questa modifica."), null);
            }

            var entry = db.Entry(entita);
            var prima = Leggi(voce.ValoriPrecedenti);
            var dopo = Leggi(voce.ValoriSuccessivi);
            var campi = new List<CampoAnnullamento>();
            foreach (var (nome, valorePrima) in prima)
            {
                var proprieta = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nome);
                if (proprieta is null)
                {
                    await db.DisposeAsync();
                    return (NonPossibile($"Il campo «{nome}» non esiste più."), null);
                }
                var clr = proprieta.Metadata.ClrType;
                var attuale = proprieta.CurrentValue;
                var cambiato = !dopo.TryGetValue(nome, out var valoreDopo) || !Equals(Converti(valoreDopo, clr), attuale);
                campi.Add(new CampoAnnullamento(nome, JsonSerializer.SerializeToElement(attuale), valorePrima, cambiato));
                if (applica) proprieta.CurrentValue = Converti(valorePrima, clr);
            }
            return (new AnteprimaAnnullamento(voceId, true, null, campi, successive), db);
        }
        return (NonPossibile("Il modulo che gestisce questo elemento non è attivo."), null);
    }

    private static Dictionary<string, JsonElement> Leggi(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    private static object? Converti(JsonElement valore, Type tipo) =>
        valore.ValueKind == JsonValueKind.Null ? null : valore.Deserialize(tipo);
}
