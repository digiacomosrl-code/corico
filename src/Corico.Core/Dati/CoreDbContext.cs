using Corico.Core.Dominio;
using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Dati;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options, IUtenteCorrente utente, TimeProvider orologio)
    : CoricoDbContextBase(options, utente, orologio)
{
    public const string TabellaMigrazioni = "__Migrazioni_CORE";

    public DbSet<Commessa> Commesse => Set<Commessa>();
    public DbSet<Cliente> Clienti => Set<Cliente>();
    public DbSet<Fornitore> Fornitori => Set<Fornitore>();
    public DbSet<Documento> Documenti => Set<Documento>();
    public DbSet<ContatoreCodice> ContatoriCodice => Set<ContatoreCodice>();
    public DbSet<Impostazione> Impostazioni => Set<Impostazione>();
    public DbSet<RegistroBackup> RegistroBackup => Set<RegistroBackup>();

    protected override bool PossiedeTabelleCore => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Commessa>(e =>
        {
            e.ToTable("CORE_Commesse");
            e.HasIndex(x => x.Codice).IsUnique();
            e.Property(x => x.Codice).HasMaxLength(20).IsRequired();
            e.Property(x => x.Descrizione).HasMaxLength(500).IsRequired();
            e.Property(x => x.CodiceEsterno).HasMaxLength(50);
            e.Property(x => x.Responsabile).HasMaxLength(200);
            e.Property(x => x.ProvinciaCantiere).HasMaxLength(5);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Documento>().WithMany().HasForeignKey(x => x.ContrattoDocumentoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("CORE_Clienti");
            e.Property(x => x.Denominazione).HasMaxLength(300).IsRequired();
            e.Property(x => x.PartitaIva).HasMaxLength(20);
            e.Property(x => x.CodiceFiscale).HasMaxLength(20);
            e.Property(x => x.Provincia).HasMaxLength(5);
            e.HasIndex(x => x.PartitaIva);
            e.HasIndex(x => x.CodiceFiscale);
        });

        modelBuilder.Entity<Fornitore>(e =>
        {
            e.ToTable("CORE_Fornitori");
            e.Property(x => x.Denominazione).HasMaxLength(300).IsRequired();
            e.Property(x => x.PartitaIva).HasMaxLength(20);
            e.Property(x => x.CodiceFiscale).HasMaxLength(20);
            e.Property(x => x.Provincia).HasMaxLength(5);
            e.HasIndex(x => x.PartitaIva);
        });

        modelBuilder.Entity<Documento>(e =>
        {
            e.ToTable("CORE_Documenti");
            e.HasIndex(x => x.Impronta);
            e.HasIndex(x => x.CommessaId);
            e.Property(x => x.NomeOriginale).HasMaxLength(260).IsRequired();
            e.Property(x => x.PercorsoInterno).HasMaxLength(400).IsRequired();
            e.Property(x => x.Impronta).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Commessa).WithMany().HasForeignKey(x => x.CommessaId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContatoreCodice>(e =>
        {
            e.ToTable("CORE_ContatoriCodice");
            e.HasKey(x => x.Anno);
            e.Property(x => x.Anno).ValueGeneratedNever();
        });

        modelBuilder.Entity<Impostazione>(e =>
        {
            e.ToTable("CORE_Impostazioni");
            e.HasKey(x => x.Chiave);
            e.Property(x => x.Chiave).HasMaxLength(100);
        });

        modelBuilder.Entity<RegistroBackup>(e =>
        {
            e.ToTable("CORE_RegistroBackup");
            e.HasIndex(x => x.DataOra);
        });

        base.OnModelCreating(modelBuilder);
    }
}
