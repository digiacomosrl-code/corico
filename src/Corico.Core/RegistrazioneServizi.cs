using Corico.Core.Backup;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Moduli;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Core;

public static class RegistrazioneServizi
{
    /// <summary>Registra i servizi del Core e dei moduli attivi.</summary>
    public static IServiceCollection AggiungiCorico(this IServiceCollection servizi, PercorsiDati percorsi, IEnumerable<ICoricoModulo> moduli)
    {
        var attivi = moduli.ToList();

        servizi.AddSingleton(percorsi);
        servizi.AddSingleton(TimeProvider.System);
        servizi.AddSingleton<IUtenteCorrente, UtenteWindows>();
        servizi.AddSingleton(new RegistroModuli(attivi));
        servizi.AddSingleton<MonitorAttivita>();

        servizi.AddDbContextFactory<CoreDbContext>(o => o.UseSqlite(percorsi.StringaConnessione,
            s => s.MigrationsHistoryTable(CoreDbContext.TabellaMigrazioni)));

        servizi.AddSingleton<IMigrazioniDatabase, MigrazioniDatabase>();
        servizi.AddSingleton<CommesseService>();
        servizi.AddSingleton<ClientiService>();
        servizi.AddSingleton<DocumentiService>();
        servizi.AddSingleton<StoricoService>();
        servizi.AddSingleton<ImpostazioniService>();
        servizi.AddSingleton<BackupService>();
        servizi.AddSingleton<AvvisiService>();
        servizi.AddHostedService<BackupAutomaticoService>();

        foreach (var modulo in attivi)
            modulo.RegistraServizi(servizi, percorsi);

        return servizi;
    }
}
