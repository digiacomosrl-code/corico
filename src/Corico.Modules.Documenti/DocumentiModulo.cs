using System.Reflection;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Moduli;
using Corico.Modules.Documenti.Dati;
using Corico.Modules.Documenti.Estrazione;
using Corico.Modules.Documenti.Interfaccia;
using Corico.Modules.Documenti.Servizi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Corico.Modules.Documenti;

/// <summary>
/// Modulo 3 — Documenti e AI. Tabelle con prefisso DOC_. Rilascio 3A: lettura del contenuto (testo, OCR, mail),
/// tipo proposto, ricerca nel contenuto e duplicati simili, tutto sul PC sede.
/// </summary>
public sealed class DocumentiModulo : ICoricoModulo
{
    public string Codice => "DOC";
    public string Nome => "Documenti e AI";
    public Assembly AssemblyInterfaccia => typeof(DocumentiModulo).Assembly;

    public IReadOnlyList<VoceMenu> VociMenu { get; } = [new("Ricerca documenti", "documenti/ricerca", Ordine: 35)];

    public IReadOnlyList<SchedaCommessa> SchedeCommessa { get; } = [new("Ricerca documenti", typeof(SchedaRicercaCommessa), Ordine: 60)];

    public void RegistraServizi(IServiceCollection servizi, PercorsiDati percorsi)
    {
        servizi.AddDbContextFactory<DocumentiDbContext>(o => o.UseSqlite(percorsi.StringaConnessione,
            s => s.MigrationsHistoryTable(DocumentiDbContext.TabellaMigrazioni)));
        servizi.AggiungiContestoAnnullabile<DocumentiDbContext>();
        servizi.AddSingleton<IMotoreOcr>(sp => new OcrTesseract(sp.GetService<IConfiguration>(), sp.GetService<ILogger<OcrTesseract>>()));
        servizi.AddSingleton<LetturaDocumento>();
        servizi.AddSingleton<IndiceRicerca>();
        servizi.AddSingleton<LetturaArchivioService>();
        servizi.AddSingleton<RicercaDocumentiService>();
        servizi.AddHostedService<CodaLettura>();
    }

    public async Task ApplicaMigrazioniAsync(IServiceProvider servizi, CancellationToken ct)
    {
        await using var db = await servizi.GetRequiredService<IDbContextFactory<DocumentiDbContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
