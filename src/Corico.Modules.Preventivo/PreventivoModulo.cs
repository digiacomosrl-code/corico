using System.Reflection;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Moduli;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Interfaccia;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Modules.Preventivo;

/// <summary>Modulo 1 — Preventivo economico di commessa. Tabelle con prefisso PRV_.</summary>
public sealed class PreventivoModulo : ICoricoModulo
{
    public string Codice => "PRV";
    public string Nome => "Preventivo";
    public Assembly AssemblyInterfaccia => typeof(PreventivoModulo).Assembly;

    public IReadOnlyList<VoceMenu> VociMenu { get; } = [new("Analisi prezzi", "analisi", Ordine: 40)];

    public IReadOnlyList<SchedaCommessa> SchedeCommessa { get; } =
    [
        new("Preventivo", typeof(SchedaPreventivo), Ordine: 10),
        new("Pacchetti", typeof(SchedaPacchetti), Ordine: 20),
        new("Richieste d'offerta", typeof(SchedaRichieste), Ordine: 22),
        new("Lista della Spesa", typeof(SchedaListaSpesa), Ordine: 25),
        new("Ordini", typeof(SchedaOrdini), Ordine: 27),
        new("Aggiorna prezzi", typeof(SchedaAggiornaPrezzi), Ordine: 28),
        new("Riepilogo economico", typeof(SchedaRiepilogo), Ordine: 30),
        new("Budget", typeof(SchedaVersioni), Ordine: 40),
    ];

    public void RegistraServizi(IServiceCollection servizi, PercorsiDati percorsi)
    {
        servizi.AddDbContextFactory<PreventivoDbContext>(o => o.UseSqlite(percorsi.StringaConnessione,
            s => s.MigrationsHistoryTable(PreventivoDbContext.TabellaMigrazioni)));
        servizi.AggiungiContestoAnnullabile<PreventivoDbContext>();
        servizi.AddSingleton<PreventivoService>();
        servizi.AddSingleton<ImportazioneComputoService>();
        servizi.AddSingleton<PacchettiService>();
        servizi.AddSingleton<ImportazionePianificazioneService>();
        servizi.AddSingleton<RiepilogoService>();
        servizi.AddSingleton<VersioniService>();
        servizi.AddSingleton<EsportazioniService>();
        servizi.AddSingleton<AnalisiService>();
        servizi.AddSingleton<ListaSpesaService>();
        servizi.AddSingleton<OrdiniService>();
        servizi.AddSingleton<BudgetService>();
        servizi.AddSingleton<RichiesteService>();
        servizi.AddSingleton<ConfrontoService>();
        servizi.AddSingleton<AggiornamentoPrezziService>();
    }

    public async Task ApplicaMigrazioniAsync(IServiceProvider servizi, CancellationToken ct)
    {
        await using var db = await servizi.GetRequiredService<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Dopo un aggiornamento: ricalcola la bozza di ogni commessa e rilegge ogni versione ufficiale,
    /// verificando che i totali della fotografia coincidano con quelli registrati alla pubblicazione.
    /// </summary>
    public async Task<IReadOnlyList<EsitoControllo>> ControlliDopoAggiornamentoAsync(IServiceProvider servizi, CancellationToken ct)
    {
        await using var db = await servizi.GetRequiredService<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync(ct);
        var riepilogo = servizi.GetRequiredService<RiepilogoService>();
        var errori = new List<string>();

        var commesse = await db.Preventivi.AsNoTracking().Select(p => p.CommessaId).Distinct().ToListAsync(ct);
        foreach (var id in commesse)
        {
            try { await riepilogo.FotografiaAsync(id, ct); }
            catch (Exception ex) { errori.Add($"commessa {id}: {ex.Message}"); }
        }
        var bozze = new EsitoControllo("Ricalcolo dei preventivi", errori.Count == 0,
            errori.Count == 0 ? $"commesse ricalcolate: {commesse.Count}" : string.Join("; ", errori.Take(5)));

        errori.Clear();
        var versioni = await db.Versioni.AsNoTracking().ToListAsync(ct);
        foreach (var v in versioni)
        {
            try
            {
                var t = VersioniService.Leggi(v).Totali;
                if (t.Ricavo != v.Ricavo || t.Costo != v.Costo)
                    errori.Add($"versione n. {v.Numero} della commessa {v.CommessaId}: totali diversi da quelli pubblicati");
            }
            catch (Exception ex) { errori.Add($"versione n. {v.Numero} della commessa {v.CommessaId}: {ex.Message}"); }
        }
        var ufficiali = new EsitoControllo("Versioni ufficiali", errori.Count == 0,
            errori.Count == 0 ? $"versioni rilette: {versioni.Count}, totali invariati" : string.Join("; ", errori.Take(5)));
        return [bozze, ufficiali];
    }

    public void MappaIndirizzi(IEndpointRouteBuilder app)
    {
        var gruppo = app.MapGroup("/preventivo/{commessaId:guid}");
        gruppo.MapGet("/computo.xlsx", (Guid commessaId, Guid? versione, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.ComputoExcelAsync(commessaId, versione, ct)));
        gruppo.MapGet("/lista-spesa.xlsx", (Guid commessaId, string? fornitore, string? cerca, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.ListaSpesaExcelAsync(commessaId, fornitore, cerca, ct)));
        gruppo.MapGet("/riepilogo.xlsx", (Guid commessaId, Guid? versione, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.RiepilogoExcelAsync(commessaId, versione, ct)));
        gruppo.MapGet("/riepilogo.pdf", (Guid commessaId, Guid? versione, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.RiepilogoPdfAsync(commessaId, versione, ct)));
        gruppo.MapGet("/pacchetti/{pacchettoId:guid}.pdf", (Guid commessaId, Guid pacchettoId, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.PacchettoPdfAsync(commessaId, pacchettoId, ct)));
        gruppo.MapGet("/ordini/{ordineId:guid}.pdf", (Guid commessaId, Guid ordineId, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.OrdinePdfAsync(commessaId, ordineId, ct)));
        gruppo.MapGet("/richieste/{richiestaId:guid}.pdf", (Guid commessaId, Guid richiestaId, Guid? destinatario, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.RichiestaAsync(commessaId, richiestaId, destinatario, excel: false, ct)));
        gruppo.MapGet("/richieste/{richiestaId:guid}.xlsx", (Guid commessaId, Guid richiestaId, Guid? destinatario, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.RichiestaAsync(commessaId, richiestaId, destinatario, excel: true, ct)));
    }

    /// <summary>Richieste d'offerta senza risposta oltre la scadenza e offerte scelte scadute o in scadenza, per commessa.</summary>
    public async Task<IReadOnlyList<Corico.Core.Servizi.Avviso>> AvvisiAsync(IServiceProvider servizi, CancellationToken ct)
    {
        var richieste = await servizi.GetRequiredService<RichiesteService>().ScaduteAsync(ct);
        var offerte = await servizi.GetRequiredService<ConfrontoService>().ScadenzeAsync(ct);
        var commesse = servizi.GetRequiredService<Corico.Core.Servizi.CommesseService>();
        var avvisi = new List<Corico.Core.Servizi.Avviso>();
        foreach (var id in richieste.Select(r => r.CommessaId).Concat(offerte.Select(o => o.CommessaId)).Distinct())
        {
            var c = await commesse.OttieniAsync(id, ct);
            if (c is null || c.Archiviata) continue;
            var parti = new List<string>();
            var rr = richieste.Where(r => r.CommessaId == id).ToList();
            if (rr.Count > 0)
                parti.Add(rr.Count == 1 ? $"la {rr[0].Numero} ha {rr[0].SenzaRisposta} fornitori senza risposta dopo la scadenza del {Corico.Core.Calcolo.Formato.Data(rr[0].Scadenza)}"
                    : $"{rr.Count} richieste d'offerta hanno fornitori senza risposta dopo la scadenza");
            if (offerte.FirstOrDefault(o => o.CommessaId == id) is { } o && (o.Scadute > 0 || o.InScadenza > 0))
                parti.Add(string.Join(" e ", new[]
                {
                    o.Scadute > 0 ? (o.Scadute == 1 ? "1 offerta scelta è scaduta" : $"{o.Scadute} offerte scelte sono scadute") : null,
                    o.InScadenza > 0 ? (o.Scadute > 0 ? $"{o.InScadenza} scadono" : o.InScadenza == 1 ? "1 offerta scelta scade" : $"{o.InScadenza} offerte scelte scadono")
                        + $" entro {ConfrontoService.GiorniPreavviso} giorni" : null,
                }.Where(x => x is not null)));
            if (parti.Count == 0) continue;
            avvisi.Add(new(Corico.Core.Servizi.GravitaAvviso.Attenzione, $"Commessa {c.Codice}: {string.Join("; ", parti)}.",
                "Sollecita i fornitori o chiedi la conferma dei prezzi.", $"/commesse/{id}"));
        }
        return avvisi;
    }

    private static async Task<IResult> Rispondi(Task<FileEsportato?> esportazione) =>
        await esportazione is { } f ? Results.File(f.Contenuto, f.TipoContenuto, f.Nome) : Results.NotFound();
}
