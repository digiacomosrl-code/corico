using System.Text.Json;
using Corico.Core.Dominio;
using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Corico.Core.Dati;

/// <summary>
/// Base comune ai DbContext del Core e dei moduli: gestisce il campo di versione
/// (controllo ottimistico), i dati di creazione/modifica e il registro delle modifiche.
/// </summary>
public abstract class CoricoDbContextBase : DbContext
{
    private static readonly JsonSerializerOptions OpzioniJson = new() { WriteIndented = false };

    private readonly IUtenteCorrente _utente;
    private readonly TimeProvider _orologio;

    protected CoricoDbContextBase(DbContextOptions options, IUtenteCorrente utente, TimeProvider orologio)
        : base(options)
    {
        _utente = utente;
        _orologio = orologio;
    }

    public DbSet<VoceStorico> Storico => Set<VoceStorico>();

    /// <summary>
    /// True solo per il DbContext del Core, proprietario della tabella dello storico.
    /// I moduli la usano senza includerla nelle proprie migrazioni.
    /// </summary>
    protected abstract bool PossiedeTabelleCore { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VoceStorico>(e =>
        {
            e.ToTable("CORE_Storico", t => { if (!PossiedeTabelleCore) t.ExcludeFromMigrations(); });
            e.HasIndex(x => x.CommessaId);
            e.HasIndex(x => new { x.Entita, x.EntitaId });
            e.Property(x => x.Utente).HasMaxLength(100);
            e.Property(x => x.Operazione).HasMaxLength(50);
            e.Property(x => x.Entita).HasMaxLength(100);
        });

        foreach (var tipo in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(EntitaBase).IsAssignableFrom(tipo.ClrType))
            {
                modelBuilder.Entity(tipo.ClrType).Property(nameof(EntitaBase.Versione)).IsConcurrencyToken();
                modelBuilder.Entity(tipo.ClrType).Property(nameof(EntitaBase.CreatoDa)).HasMaxLength(100);
                modelBuilder.Entity(tipo.ClrType).Property(nameof(EntitaBase.ModificatoDa)).HasMaxLength(100);
            }
        }
    }

    /// <summary>Nota facoltativa allegata alle voci di storico del prossimo salvataggio.</summary>
    public string? NotaStorico { get; set; }

    /// <summary>
    /// Per le importazioni massive: gli inserimenti non generano una voce di storico ciascuno;
    /// il chiamante registra un'unica operazione riepilogativa con <see cref="RegistraOperazione"/>.
    /// Modifiche ed eliminazioni restano registrate singolarmente.
    /// </summary>
    public bool InserimentiSoloInRiepilogo { get; set; }

    /// <summary>Registra un'operazione applicativa (es. cambio di stato, ripristino) nello storico.</summary>
    public void RegistraOperazione(string operazione, string entita, Guid? entitaId, Guid? commessaId, string? nota,
        object? prima = null, object? dopo = null)
    {
        Storico.Add(new VoceStorico
        {
            DataOra = _orologio.GetUtcNow().UtcDateTime,
            Utente = _utente.Nome,
            Operazione = operazione,
            Entita = entita,
            EntitaId = entitaId,
            CommessaId = commessaId,
            Nota = nota,
            ValoriPrecedenti = prima is null ? null : JsonSerializer.Serialize(prima, OpzioniJson),
            ValoriSuccessivi = dopo is null ? null : JsonSerializer.Serialize(dopo, OpzioniJson),
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreparaSalvataggio();
        return SalvaConControlloConflitti(() => base.SaveChanges(acceptAllChangesOnSuccess));
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreparaSalvataggio();
        return SalvaConControlloConflittiAsync(() => base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken));
    }

    private static int SalvaConControlloConflitti(Func<int> salva)
    {
        try { return salva(); }
        catch (DbUpdateConcurrencyException ex) { throw Conflitto(ex); }
    }

    private static async Task<int> SalvaConControlloConflittiAsync(Func<Task<int>> salva)
    {
        try { return await salva(); }
        catch (DbUpdateConcurrencyException ex) { throw Conflitto(ex); }
    }

    /// <summary>Avviso di modifica contemporanea: dice, se possibile, chi ha salvato per primo e quando.</summary>
    private static ConflittoModificaException Conflitto(DbUpdateConcurrencyException ex)
    {
        string? chi = null;
        try
        {
            var attuali = ex.Entries.FirstOrDefault()?.GetDatabaseValues();
            if (attuali is null)
                return new("I dati sono stati eliminati da un altro utente dopo che li avevi aperti. Ricarica la pagina.", ex);
            if (attuali.Properties.Any(p => p.Name == nameof(EntitaBase.ModificatoDa))
                && attuali[nameof(EntitaBase.ModificatoDa)] is string da && attuali[nameof(EntitaBase.ModificatoIl)] is DateTime il)
                chi = $"{da} alle {DateTime.SpecifyKind(il, DateTimeKind.Utc).ToLocalTime():HH:mm} del {DateTime.SpecifyKind(il, DateTimeKind.Utc).ToLocalTime():dd/MM}";
        }
        catch (Exception) { /* l'avviso resta generico */ }
        return new((chi is null
                ? "I dati sono stati modificati da un altro utente o in un'altra finestra dopo che li avevi aperti. "
                : $"I dati sono stati modificati da {chi}, dopo che li avevi aperti. ") +
            "Ricarica la pagina per vedere la versione aggiornata prima di salvare di nuovo: le tue modifiche non sono state salvate.", ex);
    }

    private void PreparaSalvataggio()
    {
        ControllaPermessi();
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var utente = _utente.Nome;
        var voci = new List<VoceStorico>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is VoceStorico) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            if (entry.Entity is EntitaBase entita)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entita.CreatoIl = adesso;
                        entita.CreatoDa = utente;
                        entita.Versione = 1;
                        break;
                    case EntityState.Modified:
                        // Il valore originale (quello letto) resta come condizione di aggiornamento.
                        var versione = entry.Property(nameof(EntitaBase.Versione));
                        versione.CurrentValue = (int)versione.OriginalValue! + 1;
                        entita.ModificatoIl = adesso;
                        entita.ModificatoDa = utente;
                        break;
                }
            }

            if (InserimentiSoloInRiepilogo && entry.State == EntityState.Added) continue;
            var voce = CreaVoceStorico(entry, adesso, utente);
            if (voce is not null) voci.Add(voce);
        }

        Storico.AddRange(voci);
        NotaStorico = null;
    }

    /// <summary>
    /// Permessi applicati a ogni salvataggio, qualunque sia la pagina di partenza:
    /// la consultazione non modifica nulla (salvo i dati tecnici creati in lettura);
    /// gli utenti li gestisce solo l'amministratore, mentre ciascuno può cambiare la propria password.
    /// Senza utente collegato (operazioni di sistema) non ci sono limiti.
    /// </summary>
    private void ControllaPermessi()
    {
        if (_utente.Identita is not { } chi) return;
        var modifiche = ChangeTracker.Entries()
            .Where(e => e.Entity is not VoceStorico && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (modifiche.Count == 0) return;

        if (!chi.Attivo)
            throw new OperazioneNonValidaException("Il tuo utente è stato disattivato: esci e rivolgiti all'amministratore.");
        if (chi.Ruolo == RuoloUtente.Consultazione && modifiche.Any(e => e.Entity is not Utente && !(e.State == EntityState.Added && e.Entity is IDatoTecnico)))
            throw new OperazioneNonValidaException("Il tuo profilo è di sola consultazione: non puoi modificare i dati.");

        if (chi.Ruolo != RuoloUtente.Amministratore && modifiche.Any(e => e.Entity is IRiservatoAmministratore && e.State == EntityState.Added))
            throw new OperazioneNonValidaException("Questa operazione è riservata all'amministratore.");

        if (chi.Ruolo != RuoloUtente.Amministratore)
        {
            string[] propri = [nameof(Utente.HashPassword), nameof(Utente.CambioPasswordObbligatorio), nameof(Utente.UltimoAccesso)];
            foreach (var e in modifiche.Where(e => e.Entity is Utente))
            {
                var ammessa = e.State == EntityState.Modified && ((Utente)e.Entity).Id == chi.Id
                    && e.Properties.Where(p => p.IsModified).All(p => propri.Contains(p.Metadata.Name) || CampiTecnici.Contains(p.Metadata.Name));
                if (!ammessa) throw new OperazioneNonValidaException("Solo l'amministratore può gestire gli utenti.");
            }
        }
    }

    private static readonly HashSet<string> CampiTecnici =
    [
        nameof(EntitaBase.Versione), nameof(EntitaBase.CreatoIl), nameof(EntitaBase.CreatoDa),
        nameof(EntitaBase.ModificatoIl), nameof(EntitaBase.ModificatoDa),
        // L'impronta della password non va mai nello storico.
        nameof(Utente.HashPassword), nameof(Utente.UltimoAccesso), nameof(Utente.CambioPasswordObbligatorio),
    ];

    private VoceStorico? CreaVoceStorico(EntityEntry entry, DateTime adesso, string utente)
    {
        Dictionary<string, object?>? prima = null, dopo = null;
        var proprieta = entry.Properties.Where(p => !CampiTecnici.Contains(p.Metadata.Name));

        switch (entry.State)
        {
            case EntityState.Added:
                dopo = proprieta.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                break;
            case EntityState.Deleted:
                prima = proprieta.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                break;
            case EntityState.Modified:
                var modificate = proprieta.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
                if (modificate.Count == 0) return null;
                prima = modificate.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                dopo = modificate.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                break;
        }

        var id = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue as Guid?;
        var commessaId = entry.Entity is Commessa c ? c.Id
            : entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CommessaId")?.CurrentValue as Guid?;

        return new VoceStorico
        {
            DataOra = adesso,
            Utente = utente,
            Operazione = entry.State switch
            {
                EntityState.Added => "Inserimento",
                EntityState.Deleted => "Eliminazione",
                _ => "Modifica",
            },
            Entita = entry.Metadata.ClrType.Name,
            EntitaId = id,
            CommessaId = commessaId,
            ValoriPrecedenti = prima is null ? null : JsonSerializer.Serialize(prima, OpzioniJson),
            ValoriSuccessivi = dopo is null ? null : JsonSerializer.Serialize(dopo, OpzioniJson),
            Nota = NotaStorico,
        };
    }
}
