using Corico.Core.Dati;
using Corico.Core.Dominio;
using Corico.Core.Utenti;
using Corico.Modules.Preventivo.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    public DbSet<RigaOrdineOrigine> RigheOrdineOrigini => Set<RigaOrdineOrigine>();
    public DbSet<RichiestaOfferta> Richieste => Set<RichiestaOfferta>();
    public DbSet<RigaRichiesta> RigheRichiesta => Set<RigaRichiesta>();
    public DbSet<DestinatarioRichiesta> DestinatariRichiesta => Set<DestinatarioRichiesta>();
    public DbSet<StimaEsclusione> StimeEsclusioni => Set<StimaEsclusione>();
    public DbSet<AggiornamentoPrezzi> AggiornamentiPrezzi => Set<AggiornamentoPrezzi>();
    public DbSet<RigaAggiornamento> RigheAggiornamento => Set<RigaAggiornamento>();
    public DbSet<ElementoWbs> ElementiWbs => Set<ElementoWbs>();

    protected override bool PossiedeTabelleCore => false;

    /// <summary>
    /// Vero solo durante l'adozione di una perizia di variante (rilascio 2C): consente di cambiare i prezzi contrattuali
    /// anche dopo l'approvazione di un budget.
    /// </summary>
    public bool AdozionePerizia { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Proteggi();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Proteggi();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Proteggi()
    {
        ProteggiVersioni();
        ProteggiOrdini();
        ProteggiPrezziContrattuali();
    }

    private static readonly string[] CampiTecniciModifica =
        [nameof(EntitaBase.Versione), nameof(EntitaBase.ModificatoIl), nameof(EntitaBase.ModificatoDa)];

    private static bool SoloCampi(EntityEntry e, IEnumerable<string> ammessi) =>
        e.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
            .All(p => ammessi.Contains(p.Metadata.Name) || CampiTecniciModifica.Contains(p.Metadata.Name));

    /// <summary>
    /// Le versioni ufficiali e i budget sono immutabili: si possono solo aggiungere. Di un budget in approvazione
    /// cambia soltanto l'esito (approvato o respinto), e solo per mano dell'amministratore.
    /// Le versioni storiche senza budget le pubblica solo l'amministratore.
    /// </summary>
    private void ProteggiVersioni()
    {
        var amministratore = UtenteCollegato is null || UtenteCollegato.Ruolo == RuoloUtente.Amministratore;
        string[] esito = [nameof(VersionePreventivo.StatoBudget), nameof(VersionePreventivo.EsitoDa), nameof(VersionePreventivo.EsitoIl),
            nameof(VersionePreventivo.MotivoRifiuto)];
        foreach (var e in ChangeTracker.Entries<VersionePreventivo>())
        {
            switch (e.State)
            {
                case EntityState.Added when !e.Entity.EBudget && !amministratore:
                    throw new OperazioneNonValidaException("Questa operazione è riservata all'amministratore.");
                case EntityState.Added when e.Entity.EBudget && e.Entity.StatoBudget != StatoBudget.InApprovazione:
                    throw new OperazioneNonValidaException("Un nuovo budget nasce sempre in approvazione.");
                case EntityState.Deleted:
                    throw new OperazioneNonValidaException("Le versioni ufficiali del preventivo non si possono modificare né eliminare.");
                case EntityState.Modified:
                    var statoLetto = (StatoBudget?)e.Property(nameof(VersionePreventivo.StatoBudget)).OriginalValue;
                    if (statoLetto != StatoBudget.InApprovazione || !SoloCampi(e, esito))
                        throw new OperazioneNonValidaException(statoLetto == StatoBudget.Approvato
                            ? "Il budget approvato non si può più modificare."
                            : "Le versioni ufficiali del preventivo non si possono modificare né eliminare.");
                    if (!amministratore)
                        throw new OperazioneNonValidaException("L'approvazione del budget è riservata all'amministratore.");
                    break;
            }
        }
    }

    /// <summary>
    /// Ordini di acquisto: in bozza si modificano e si eliminano liberamente; dopo l'emissione cambiano solo lo stato,
    /// i dati dell'annullamento e le date facoltative di invio e conferma.
    /// </summary>
    private void ProteggiOrdini()
    {
        string[] dopoEmissione = [nameof(OrdineAcquisto.Stato), nameof(OrdineAcquisto.MotivoAnnullamento), nameof(OrdineAcquisto.AnnullatoIl),
            nameof(OrdineAcquisto.InviatoIl), nameof(OrdineAcquisto.ConfermatoIl), nameof(OrdineAcquisto.ConfermaDocumentoId)];
        foreach (var e in ChangeTracker.Entries<OrdineAcquisto>())
        {
            var statoLetto = (StatoOrdine)e.Property(nameof(OrdineAcquisto.Stato)).OriginalValue!;
            if (e.State is EntityState.Modified or EntityState.Deleted && statoLetto == StatoOrdine.Bozza) continue;
            if (e.State == EntityState.Deleted || (e.State == EntityState.Modified && !SoloCampi(e, dopoEmissione)))
                throw new OperazioneNonValidaException("Un ordine emesso non si modifica: va annullato ed emesso di nuovo.");
        }

        var righe = ChangeTracker.Entries<RigaOrdine>().Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.OrdineId)
            .Concat(ChangeTracker.Entries<RigaOrdineOrigine>().Where(e => e.State is EntityState.Modified or EntityState.Deleted)
                .Select(e => e.Entity.OrdineId))
            .Distinct().ToList();
        if (righe.Count == 0) return;
        // Lo stato letto dal database: una bozza appena emessa nello stesso salvataggio non è più modificabile.
        var bozze = Ordini.AsNoTracking().Where(o => righe.Contains(o.Id) && o.Stato == StatoOrdine.Bozza).Select(o => o.Id).ToHashSet();
        if (righe.Any(id => !bozze.Contains(id)))
            throw new OperazioneNonValidaException("Le righe di un ordine emesso non si modificano.");
    }

    /// <summary>
    /// Dopo l'approvazione di un budget i prezzi contrattuali (computo, prezzi, quantità, quote non soggette e ribasso d'asta)
    /// sono bloccati anche nella bozza: cambiano solo con una perizia di variante.
    /// </summary>
    private void ProteggiPrezziContrattuali()
    {
        if (AdozionePerizia) return;
        string[] campiVoce = [nameof(VoceComputo.Quantita), nameof(VoceComputo.Prezzo), nameof(VoceComputo.PercentualeNonSoggetta),
            nameof(VoceComputo.Archiviata), nameof(VoceComputo.ComputoId)];
        var commesse = new HashSet<Guid>();
        foreach (var e in ChangeTracker.Entries<VoceComputo>())
        {
            if (e.State is EntityState.Added or EntityState.Deleted
                || (e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && campiVoce.Contains(p.Metadata.Name)
                    && !Equals(p.OriginalValue, p.CurrentValue))))
                commesse.Add(e.Entity.CommessaId);
        }
        foreach (var e in ChangeTracker.Entries<Computo>())
        {
            if (e.State is EntityState.Added or EntityState.Deleted
                || (e.State == EntityState.Modified && e.Property(nameof(Computo.Attivo)).IsModified
                    && !Equals(e.Property(nameof(Computo.Attivo)).OriginalValue, e.Entity.Attivo)))
                commesse.Add(e.Entity.CommessaId);
        }
        foreach (var e in ChangeTracker.Entries<Dominio.Preventivo>())
        {
            if (e.State == EntityState.Modified && e.Property(nameof(Dominio.Preventivo.RibassoPercentuale)).IsModified
                && (decimal)e.Property(nameof(Dominio.Preventivo.RibassoPercentuale)).OriginalValue! != e.Entity.RibassoPercentuale)
                commesse.Add(e.Entity.CommessaId);
        }
        if (commesse.Count == 0) return;
        if (Versioni.AsNoTracking().Any(v => commesse.Contains(v.CommessaId) && v.StatoBudget == StatoBudget.Approvato))
            throw new OperazioneNonValidaException(
                "Il budget della commessa è approvato: i prezzi contrattuali (computo, prezzi, quantità e ribasso) sono bloccati " +
                "e cambiano solo con una perizia di variante.");
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
            e.Property(x => x.CodiceWbs).HasMaxLength(CodiciWbs.LunghezzaMassima);
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
            e.Property(x => x.OffertaSceltaDa).HasMaxLength(100);
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
            e.HasMany(x => x.StimeEsclusioni).WithOne().HasForeignKey(x => x.OffertaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StimaEsclusione>(e =>
        {
            e.ToTable("PRV_StimeEsclusioni");
            e.HasIndex(x => new { x.OffertaId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<RichiestaOfferta>(e =>
        {
            e.ToTable("PRV_RichiesteOfferta");
            e.HasIndex(x => new { x.Anno, x.Progressivo }).IsUnique();
            e.HasIndex(x => x.CommessaId);
            e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
            e.Property(x => x.Oggetto).HasMaxLength(500).IsRequired();
            e.HasMany(x => x.Righe).WithOne().HasForeignKey(r => r.RichiestaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Destinatari).WithOne().HasForeignKey(r => r.RichiestaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaRichiesta>(e =>
        {
            e.ToTable("PRV_RigheRichiesta");
            e.HasIndex(x => new { x.RichiestaId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Codice).HasMaxLength(100);
            e.Property(x => x.Unita).HasMaxLength(30);
        });

        modelBuilder.Entity<DestinatarioRichiesta>(e =>
        {
            e.ToTable("PRV_DestinatariRichiesta");
            e.HasIndex(x => x.RichiestaId);
            e.Property(x => x.Fornitore).HasMaxLength(300).IsRequired();
        });

        modelBuilder.Entity<AggiornamentoPrezzi>(e =>
        {
            e.ToTable("PRV_AggiornamentiPrezzi");
            e.HasIndex(x => x.CommessaId);
            e.HasMany(x => x.Righe).WithOne().HasForeignKey(r => r.AggiornamentoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaAggiornamento>(e =>
        {
            e.ToTable("PRV_RigheAggiornamento");
            e.HasIndex(x => x.AggiornamentoId);
            e.Property(x => x.Descrizione).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Campo).HasMaxLength(40);
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
            // Le bozze non hanno numero (progressivo zero): l'unicità vale solo per gli ordini emessi.
            e.HasIndex(x => new { x.Anno, x.Progressivo }).IsUnique().HasFilter("\"Progressivo\" > 0");
            e.HasIndex(x => x.CommessaId);
            e.HasIndex(x => x.PacchettoId);
            e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
            e.Property(x => x.Fornitore).HasMaxLength(300).IsRequired();
            e.Property(x => x.EmessoDa).HasMaxLength(100);
            e.Property(x => x.Oggetto).HasMaxLength(500).IsRequired();
            e.HasMany(x => x.Righe).WithOne().HasForeignKey(r => r.OrdineId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaOrdine>(e =>
        {
            e.ToTable("PRV_RigheOrdine");
            e.HasIndex(x => new { x.OrdineId, x.Ordine });
            e.Property(x => x.Descrizione).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Codice).HasMaxLength(60);
            e.Property(x => x.Wbs).HasMaxLength(200);
            e.Property(x => x.Unita).HasMaxLength(30);
            e.HasMany(x => x.Origini).WithOne().HasForeignKey(o => o.RigaOrdineId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RigaOrdineOrigine>(e =>
        {
            e.ToTable("PRV_RigheOrdineOrigini");
            e.HasIndex(x => x.OrdineId);
            e.HasIndex(x => new { x.CommessaId, x.Chiave });
            e.Property(x => x.Chiave).HasMaxLength(800).IsRequired();
        });

        modelBuilder.Entity<CodiceRisorsa>(e =>
        {
            e.ToTable("PRV_CodiciRisorse");
            e.HasIndex(x => new { x.CommessaId, x.Categoria, x.Chiave }).IsUnique();
            e.HasIndex(x => new { x.CommessaId, x.Codice }).IsUnique();
            e.Property(x => x.Chiave).HasMaxLength(600).IsRequired();
            e.Property(x => x.Codice).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<ElementoWbs>(e =>
        {
            e.ToTable("PRV_Wbs");
            e.HasIndex(x => new { x.CommessaId, x.Codice }).IsUnique();
            e.Property(x => x.Codice).HasMaxLength(CodiciWbs.LunghezzaMassima).IsRequired();
            e.Property(x => x.Descrizione).HasMaxLength(300).IsRequired();
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
