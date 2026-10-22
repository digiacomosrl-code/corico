using Corico.Core.Dati;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Dati;

/// <summary>Tabelle del modulo Preventivo (prefisso PRV_), sullo stesso database del Core.</summary>
public sealed class PreventivoDbContext(DbContextOptions<PreventivoDbContext> options, IUtenteCorrente utente, TimeProvider orologio)
    : CoricoDbContextBase(options, utente, orologio)
{
    public const string TabellaMigrazioni = "__Migrazioni_PRV";

    public DbSet<Dominio.Preventivo> Preventivi => Set<Dominio.Preventivo>();
    public DbSet<Computo> Computi => Set<Computo>();
    public DbSet<Categoria> Categorie => Set<Categoria>();
    public DbSet<VoceElencoPrezzi> ElencoPrezzi => Set<VoceElencoPrezzi>();
    public DbSet<RigaAnalisi> RigheAnalisi => Set<RigaAnalisi>();
    public DbSet<VoceComputo> VociComputo => Set<VoceComputo>();
    public DbSet<RigaMisura> RigheMisura => Set<RigaMisura>();

    protected override bool PossiedeTabelleCore => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Le relazioni con le tabelle del Core (commesse, documenti) sono per identificativo,
        // senza chiavi esterne: un modulo non dipende dallo schema di un altro.
        modelBuilder.Entity<Dominio.Preventivo>(e =>
        {
            e.ToTable("PRV_Preventivi");
            e.HasIndex(x => x.CommessaId).IsUnique();
            e.Property(x => x.RibassoPercentuale).HasPrecision(9, 6);
        });

        modelBuilder.Entity<Computo>(e =>
        {
            e.ToTable("PRV_Computi");
            e.HasIndex(x => x.CommessaId);
            e.Property(x => x.Descrizione).HasMaxLength(300).IsRequired();
            e.HasOne<Dominio.Preventivo>().WithMany().HasForeignKey(x => x.PreventivoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Categoria>(e =>
        {
            e.ToTable("PRV_Categorie");
            e.HasIndex(x => new { x.ComputoId, x.Livello, x.IdOriginale });
            e.HasOne<Computo>().WithMany().HasForeignKey(x => x.ComputoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VoceElencoPrezzi>(e =>
        {
            e.ToTable("PRV_ElencoPrezzi");
            e.HasIndex(x => new { x.ComputoId, x.Tariffa });
            e.Property(x => x.Tariffa).HasMaxLength(100);
            e.Property(x => x.UnitaOriginale).HasMaxLength(30);
            e.Property(x => x.Unita).HasMaxLength(30);
            e.HasOne<Computo>().WithMany().HasForeignKey(x => x.ComputoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.RigheAnalisi).WithOne().HasForeignKey(x => x.VoceElencoPrezziId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaAnalisi>(e => e.ToTable("PRV_RigheAnalisi"));

        modelBuilder.Entity<VoceComputo>(e =>
        {
            e.ToTable("PRV_VociComputo");
            e.HasIndex(x => new { x.ComputoId, x.Ordine });
            e.Property(x => x.Tariffa).HasMaxLength(100);
            e.Property(x => x.Unita).HasMaxLength(30);
            e.HasOne<Computo>().WithMany().HasForeignKey(x => x.ComputoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.VoceElencoPrezzi).WithMany().HasForeignKey(x => x.VoceElencoPrezziId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.RigheMisura).WithOne().HasForeignKey(x => x.VoceComputoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaMisura>(e => e.ToTable("PRV_RigheMisura"));

        base.OnModelCreating(modelBuilder);
    }
}
