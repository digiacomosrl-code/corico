using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Corico.Modules.Documenti.Dati;

/// <summary>Usata solo dagli strumenti "dotnet ef" per generare le migrazioni del modulo.</summary>
internal sealed class DocumentiDbContextFactoryProgettazione : IDesignTimeDbContextFactory<DocumentiDbContext>
{
    public DocumentiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DocumentiDbContext>()
            .UseSqlite("Data Source=progettazione.db", o => o.MigrationsHistoryTable(DocumentiDbContext.TabellaMigrazioni))
            .Options;
        return new DocumentiDbContext(options, new UtenteCorrente(), TimeProvider.System);
    }
}
