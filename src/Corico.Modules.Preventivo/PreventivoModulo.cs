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
        new("Lista della Spesa", typeof(SchedaListaSpesa), Ordine: 25),
        new("Riepilogo economico", typeof(SchedaRiepilogo), Ordine: 30),
        new("Versioni", typeof(SchedaVersioni), Ordine: 40),
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
    }

    public async Task ApplicaMigrazioniAsync(IServiceProvider servizi, CancellationToken ct)
    {
        await using var db = await servizi.GetRequiredService<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }

    public void MappaIndirizzi(IEndpointRouteBuilder app)
    {
        var gruppo = app.MapGroup("/preventivo/{commessaId:guid}");
        gruppo.MapGet("/computo.xlsx", (Guid commessaId, Guid? versione, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.ComputoExcelAsync(commessaId, versione, ct)));
        gruppo.MapGet("/lista-spesa.xlsx", (Guid commessaId, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.ListaSpesaExcelAsync(commessaId, ct)));
        gruppo.MapGet("/riepilogo.xlsx", (Guid commessaId, Guid? versione, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.RiepilogoExcelAsync(commessaId, versione, ct)));
        gruppo.MapGet("/riepilogo.pdf", (Guid commessaId, Guid? versione, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.RiepilogoPdfAsync(commessaId, versione, ct)));
        gruppo.MapGet("/pacchetti/{pacchettoId:guid}.pdf", (Guid commessaId, Guid pacchettoId, EsportazioniService e, CancellationToken ct) =>
            Rispondi(e.PacchettoPdfAsync(commessaId, pacchettoId, ct)));
    }

    private static async Task<IResult> Rispondi(Task<FileEsportato?> esportazione) =>
        await esportazione is { } f ? Results.File(f.Contenuto, f.TipoContenuto, f.Nome) : Results.NotFound();
}
