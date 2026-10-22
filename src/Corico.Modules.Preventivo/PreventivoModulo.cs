using System.Reflection;
using Corico.Core.Configurazione;
using Corico.Core.Moduli;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Interfaccia;
using Corico.Modules.Preventivo.Servizi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Modules.Preventivo;

/// <summary>Modulo 1 — Preventivo economico di commessa. Tabelle con prefisso PRV_.</summary>
public sealed class PreventivoModulo : ICoricoModulo
{
    public string Codice => "PRV";
    public string Nome => "Preventivo";
    public Assembly AssemblyInterfaccia => typeof(PreventivoModulo).Assembly;

    public IReadOnlyList<VoceMenu> VociMenu { get; } = [];

    public IReadOnlyList<SchedaCommessa> SchedeCommessa { get; } =
    [
        new("Preventivo", typeof(SchedaPreventivo), Ordine: 10),
        new("Pacchetti", typeof(SchedaPacchetti), Ordine: 20),
    ];

    public void RegistraServizi(IServiceCollection servizi, PercorsiDati percorsi)
    {
        servizi.AddDbContextFactory<PreventivoDbContext>(o => o.UseSqlite(percorsi.StringaConnessione,
            s => s.MigrationsHistoryTable(PreventivoDbContext.TabellaMigrazioni)));
        servizi.AddSingleton<PreventivoService>();
        servizi.AddSingleton<ImportazioneComputoService>();
        servizi.AddSingleton<PacchettiService>();
        servizi.AddSingleton<ImportazionePianificazioneService>();
    }

    public async Task ApplicaMigrazioniAsync(IServiceProvider servizi, CancellationToken ct)
    {
        await using var db = await servizi.GetRequiredService<IDbContextFactory<PreventivoDbContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
