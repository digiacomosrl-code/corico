using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Corico.Core.Dati;

/// <summary>Usata solo dagli strumenti "dotnet ef" per generare le migrazioni.</summary>
internal sealed class CoreDbContextFactoryProgettazione : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite("Data Source=progettazione.db", o => o.MigrationsHistoryTable(CoreDbContext.TabellaMigrazioni))
            .Options;
        return new CoreDbContext(options, new UtenteWindows(), TimeProvider.System);
    }
}
