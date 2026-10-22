using Corico.Core.Dati;
using Corico.Core.Dominio;
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
    public DbSet<Pacchetto> Pacchetti => Set<Pacchetto>();
    public DbSet<PacchettoVoce> PacchettiVoci => Set<PacchettoVoce>();
    public DbSet<NotaPacchetto> NotePacchetti => Set<NotaPacchetto>();
    public DbSet<Offerta> Offerte => Set<Offerta>();
    public DbSet<OffertaVoce> OfferteVoci => Set<OffertaVoce>();
    public DbSet<RigaOfferta> RigheOfferta => Set<RigaOfferta>();
    public DbSet<CostoACorpo> CostiACorpo => Set<CostoACorpo>();
    public DbSet<SpesaGenerale> SpeseGenerali => Set<SpesaGenerale>();
    public DbSet<IntegrazioneRicavo> IntegrazioniRicavo => Set<IntegrazioneRicavo>();
    public DbSet<VersionePreventivo> Versioni => Set<VersionePreventivo>();
    public DbSet<RisorsaVoce> RisorseVoci => Set<RisorsaVoce>();
    public DbSet<AnalisiAziendale> AnalisiAziendali => Set<AnalisiAziendale>();
    public DbSet<RigaAnalisiAziendale> RigheAnalisiAziendali => Set<RigaAnalisiAziendale>();
    public DbSet<CodiceRisorsa> CodiciRisorse => Set<CodiceRisorsa>();
    public DbSet<OrdineAcquisto> Ordini => Set<OrdineAcquisto>();
    public DbSet<RigaOrdine> RigheOrdine => Set<RigaOrdine>();

    protected override bool PossiedeTabelleCore => false;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProteggiVersioni();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProteggiVersioni();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Le versioni ufficiali sono immutabili: si possono solo aggiungere.</summary>
    private void ProteggiVersioni()
    {
        if (ChangeTracker.Entries<VersionePreventivo>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new OperazioneNonValidaException("Le versioni ufficiali del preventivo non si possono modificare né eliminare.");

        // Ordini di acquisto: dopo l'emissione cambiano solo lo stato e i dati dell'annullamento.
        string[] ammessi = [nameof(OrdineAcquisto.Stato), nameof(OrdineAcquisto.MotivoAnnullamento), nameof(OrdineAcquisto.AnnullatoIl),
            nameof(EntitaBase.Versione), nameof(EntitaBase.ModificatoIl), nameof(EntitaBase.ModificatoDa)];
        foreach (var e in ChangeTracker.Entries<OrdineAcquisto>())
        {
            if (e.State == EntityState.Deleted
                || (e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && !ammessi.Contains(p.Metadata.Name))))
                throw new OperazioneNonValidaException("Un ordine emesso non si modifica: va annullato ed emesso di nuovo.");
        }
        if (ChangeTracker.Entries<RigaOrdine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new OperazioneNonValidaException("Le righe di un ordine emesso non si modificano.");
    }

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

        modelBuilder.Entity<Pacchetto>(e =>
        {
            e.ToTable("PRV_Pacchetti");
            e.HasIndex(x => new { x.CommessaId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(300).IsRequired();
            e.Property(x => x.Codice).HasMaxLength(100);
            e.Property(x => x.RiferimentoOda).HasMaxLength(200);
            e.HasMany(x => x.Voci).WithOne().HasForeignKey(x => x.PacchettoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PacchettoVoce>(e =>
        {
            e.ToTable("PRV_PacchettiVoci");
            // Una voce del computo appartiene a un solo pacchetto (sez. 14.1).
            e.HasIndex(x => x.VoceComputoId).IsUnique();
            e.HasOne<VoceComputo>().WithMany().HasForeignKey(x => x.VoceComputoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotaPacchetto>(e =>
        {
            e.ToTable("PRV_NotePacchetti");
            e.HasIndex(x => x.PacchettoId);
            e.Property(x => x.Iniziali).HasMaxLength(10);
            e.HasOne<Pacchetto>().WithMany().HasForeignKey(x => x.PacchettoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Offerta>(e =>
        {
            e.ToTable("PRV_Offerte");
            e.HasIndex(x => x.PacchettoId);
            e.Property(x => x.NomeFornitore).HasMaxLength(200);
            e.HasOne<Pacchetto>().WithMany().HasForeignKey(x => x.PacchettoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Voci).WithOne().HasForeignKey(x => x.OffertaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Righe).WithOne().HasForeignKey(x => x.OffertaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CostiACorpo).WithOne().HasForeignKey(x => x.OffertaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OffertaVoce>(e =>
        {
            e.ToTable("PRV_OfferteVoci");
            e.HasIndex(x => new { x.OffertaId, x.VoceComputoId }).IsUnique();
            e.HasMany(x => x.Risorse).WithOne().HasForeignKey(x => x.OffertaVoceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RisorsaVoce>(e =>
        {
            e.ToTable("PRV_RisorseVoci");
            e.HasIndex(x => new { x.OffertaVoceId, x.Ordine });
            e.HasIndex(x => x.CommessaId);
            e.Property(x => x.Descrizione).HasMaxLength(500).IsRequired();
            e.Property(x => x.Unita).HasMaxLength(30);
        });

        // Analisi aziendali: non appartengono a una commessa, sono riutilizzabili in tutte.
        modelBuilder.Entity<AnalisiAziendale>(e =>
        {
            e.ToTable("PRV_AnalisiAziendali");
            e.HasIndex(x => x.Codice);
            e.Property(x => x.Descrizione).HasMaxLength(500).IsRequired();
            e.Property(x => x.Unita).HasMaxLength(30);
            e.HasMany(x => x.Righe).WithOne().HasForeignKey(x => x.AnalisiId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaAnalisiAziendale>(e =>
        {
            e.ToTable("PRV_RigheAnalisiAziendali");
            e.HasIndex(x => new { x.AnalisiId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(500).IsRequired();
            e.Property(x => x.Unita).HasMaxLength(30);
        });

        modelBuilder.Entity<OrdineAcquisto>(e =>
        {
            e.ToTable("PRV_OrdiniAcquisto");
            e.HasIndex(x => new { x.Anno, x.Progressivo }).IsUnique();
            e.HasIndex(x => x.CommessaId);
            e.HasIndex(x => x.PacchettoId);
            e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
            e.Property(x => x.Fornitore).HasMaxLength(300).IsRequired();
            e.Property(x => x.Oggetto).HasMaxLength(500).IsRequired();
            e.HasMany(x => x.Righe).WithOne().HasForeignKey(r => r.OrdineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RigaOrdine>(e =>
        {
            e.ToTable("PRV_RigheOrdine");
            e.HasIndex(x => new { x.OrdineId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Codice).HasMaxLength(60);
            e.Property(x => x.Unita).HasMaxLength(30);
        });

        modelBuilder.Entity<CodiceRisorsa>(e =>
        {
            e.ToTable("PRV_CodiciRisorse");
            e.HasIndex(x => new { x.CommessaId, x.Categoria, x.Chiave }).IsUnique();
            e.HasIndex(x => new { x.CommessaId, x.Codice }).IsUnique();
            e.Property(x => x.Chiave).HasMaxLength(600).IsRequired();
            e.Property(x => x.Codice).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<RigaOfferta>(e =>
        {
            e.ToTable("PRV_RigheOfferta");
            e.HasIndex(x => new { x.OffertaId, x.Ordine });
        });

        modelBuilder.Entity<CostoACorpo>(e => e.ToTable("PRV_CostiACorpo"));

        modelBuilder.Entity<SpesaGenerale>(e =>
        {
            e.ToTable("PRV_SpeseGenerali");
            e.HasIndex(x => new { x.CommessaId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(300).IsRequired();
        });

        modelBuilder.Entity<IntegrazioneRicavo>(e =>
        {
            e.ToTable("PRV_IntegrazioniRicavo");
            e.HasIndex(x => x.CommessaId);
            e.Property(x => x.Descrizione).HasMaxLength(300).IsRequired();
            e.HasOne<Pacchetto>().WithMany().HasForeignKey(x => x.PacchettoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VersionePreventivo>(e =>
        {
            e.ToTable("PRV_Versioni");
            e.HasIndex(x => new { x.CommessaId, x.Numero }).IsUnique();
            e.Property(x => x.Nota).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
