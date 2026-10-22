using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Corico.Modules.Preventivo.Dati;

/// <summary>Usata solo dagli strumenti "dotnet ef" per generare le migrazioni del modulo.</summary>
internal sealed class PreventivoDbContextFactoryProgettazione : IDesignTimeDbContextFactory<PreventivoDbContext>
{
    public PreventivoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PreventivoDbContext>()
            .UseSqlite("Data Source=progettazione.db", o => o.MigrationsHistoryTable(PreventivoDbContext.TabellaMigrazioni))
            .Options;
        return new PreventivoDbContext(options, new UtenteWindows(), TimeProvider.System);
    }
}
