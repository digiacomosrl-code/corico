using Corico.Core.Dati;
using Corico.Core.Utenti;
using Corico.Modules.Documenti.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Documenti.Dati;

/// <summary>Tabelle del modulo Documenti e AI (prefisso DOC_), sullo stesso database del Core.</summary>
public sealed class DocumentiDbContext(DbContextOptions<DocumentiDbContext> options, IUtenteCorrente utente, TimeProvider orologio)
    : CoricoDbContextBase(options, utente, orologio)
{
    public const string TabellaMigrazioni = "__Migrazioni_DOC";

    public DbSet<TestoDocumento> Testi => Set<TestoDocumento>();
    public DbSet<PaginaTesto> Pagine => Set<PaginaTesto>();
    public DbSet<AllegatoMail> AllegatiMail => Set<AllegatoMail>();

    protected override bool PossiedeTabelleCore => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // I documenti sono del Core: collegamento per identificativo, senza chiavi esterne.
        modelBuilder.Entity<TestoDocumento>(e =>
        {
            e.ToTable("DOC_Testi");
            e.HasIndex(x => x.DocumentoId).IsUnique();
            e.Property(x => x.Esito).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Metodo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TipoProposto).HasMaxLength(100);
            e.Property(x => x.MotivoTipo).HasMaxLength(500);
            e.Property(x => x.MailMittente).HasMaxLength(300);
            e.Property(x => x.MailOggetto).HasMaxLength(500);
            e.HasMany(x => x.Pagine).WithOne().HasForeignKey(p => p.TestoDocumentoId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PaginaTesto>(e =>
        {
            e.ToTable("DOC_Pagine");
            e.HasIndex(x => new { x.TestoDocumentoId, x.Numero }).IsUnique();
        });
        modelBuilder.Entity<AllegatoMail>(e =>
        {
            e.ToTable("DOC_AllegatiMail");
            e.HasIndex(x => x.MailDocumentoId);
            e.HasIndex(x => x.AllegatoDocumentoId);
            e.Property(x => x.NomeFile).HasMaxLength(300);
        });
        base.OnModelCreating(modelBuilder);
    }
}
