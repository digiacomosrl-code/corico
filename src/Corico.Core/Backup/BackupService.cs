using System.Globalization;
using System.Text.Json;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Corico.Core.Backup;

public enum MotivoBackup
{
    Manuale,
    Periodico,
    AvvioGiornaliero,
    Chiusura,
    PrimaDiImportazione,
    PrimaDiPubblicazione,
    PrimaDiRipristino,
    PrimaDiAggiornamento,
    PrimaDiCancellazione,
}

public sealed record FileBackup(string Percorso, string Nome, DateTime DataOra, string Motivo, long Dimensione);

public sealed record EsitoBackup(bool Riuscito, string Messaggio, string? Percorso = null, int DocumentiCopiati = 0);

public sealed record DiscoEsterno(Guid Id, string Nome, DateTime RegistratoIl);

/// <summary>
/// Backup e ripristino (sez. 6). Il database viene copiato con l'API di backup nativa di SQLite,
/// che produce una copia coerente anche ad applicazione aperta.
/// </summary>
public sealed class BackupService(
    PercorsiDati percorsi,
    IOptions<CoricoOpzioni> opzioni,
    IDbContextFactory<CoreDbContext> fabbrica,
    ImpostazioniService impostazioni,
    IMigrazioniDatabase migrazioni,
    IUtenteCorrente utente,
    TimeProvider orologio,
    ILogger<BackupService> log)
{
    public const string FileIdentitaDisco = "CORICO-DISCO.json";
    public const string CartellaBackupEsterno = "CORICO-Backup";
    private const string PrefissoFile = "corico_";
    private const string FormatoData = "yyyyMMdd_HHmmss";

    private static readonly SemaphoreSlim Blocco = new(1, 1);

    // ------------------------------------------------------------------ backup locale

    public async Task<EsitoBackup> EseguiBackupLocaleAsync(MotivoBackup motivo, CancellationToken ct = default)
    {
        await Blocco.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(percorsi.BackupDatabase);
            var adesso = orologio.GetLocalNow().DateTime;
            var destinazione = Path.Combine(percorsi.BackupDatabase, NomeFile(adesso, motivo.ToString()));

            CopiaDatabase(percorsi.StringaConnessione, destinazione);
            VerificaIntegrita(destinazione);
            var impronta = await DocumentiService.CalcolaImprontaAsync(destinazione, ct);
            var copiati = CopiaIncrementale(percorsi.Documenti, percorsi.BackupDocumenti);

            await RegistraAsync(TipoBackup.Locale, motivo.ToString(), true, destinazione, new FileInfo(destinazione).Length,
                impronta, copiati, null, ct);
            ApplicaConservazione();
            log.LogInformation("Backup locale ({Motivo}) completato: {File}", motivo, destinazione);
            return new EsitoBackup(true, "Backup completato e verificato.", destinazione, copiati);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Backup locale ({Motivo}) non riuscito", motivo);
            await RegistraSenzaErroriAsync(TipoBackup.Locale, motivo.ToString(), ex.Message);
            return new EsitoBackup(false, "Backup non riuscito: " + ex.Message);
        }
        finally
        {
            Blocco.Release();
        }
    }

    /// <summary>
    /// Copia di sicurezza prima di un aggiornamento del programma (sez. 5.3). La esegue la nuova versione prima
    /// delle migrazioni, quindi non scrive nel database, il cui schema è ancora quello della versione installata:
    /// la copia viene registrata dopo, con <see cref="RegistraCopiaAggiornamentoAsync"/>.
    /// </summary>
    public static string CopiaPrimaDiAggiornamento(PercorsiDati percorsi, DateTime adesso)
    {
        Directory.CreateDirectory(percorsi.BackupDatabase);
        var destinazione = Path.Combine(percorsi.BackupDatabase, NomeFile(adesso, MotivoBackup.PrimaDiAggiornamento.ToString()));
        CopiaDatabase(percorsi.StringaConnessione, destinazione);
        VerificaIntegrita(destinazione);
        return destinazione;
    }

    public async Task RegistraCopiaAggiornamentoAsync(string percorso, CancellationToken ct = default)
    {
        var impronta = await DocumentiService.CalcolaImprontaAsync(percorso, ct);
        await RegistraAsync(TipoBackup.Locale, MotivoBackup.PrimaDiAggiornamento.ToString(), true, percorso,
            new FileInfo(percorso).Length, impronta, 0, null, ct);
    }

    public IReadOnlyList<FileBackup> ElencoBackupLocali() => ElencoBackup(percorsi.BackupDatabase);

    public async Task<IReadOnlyList<RegistroBackup>> RegistroAsync(int massimo = 100, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.RegistroBackup.AsNoTracking().OrderByDescending(r => r.Id).Take(massimo).ToListAsync(ct);
    }

    public async Task<RegistroBackup?> UltimoAsync(TipoBackup tipo, bool soloRiusciti, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.RegistroBackup.AsNoTracking()
            .Where(r => r.Tipo == tipo && (!soloRiusciti || r.Riuscito))
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
    }

    // ------------------------------------------------------------------ ripristino

    /// <summary>
    /// Ripristina il database da un backup: verifica il file, salva lo stato attuale,
    /// copia il backup sul database in uso e riapplica le migrazioni.
    /// L'archivio documenti non viene toccato (i file non vengono mai cancellati).
    /// </summary>
    public async Task<EsitoBackup> RipristinaAsync(string fileBackup, CancellationToken ct = default)
    {
        if (!File.Exists(fileBackup))
            return new EsitoBackup(false, "Il file di backup non esiste.");

        try
        {
            VerificaIntegrita(fileBackup);
        }
        catch (Exception ex)
        {
            return new EsitoBackup(false, "Il backup scelto non è integro e non può essere ripristinato: " + ex.Message);
        }

        var preventivo = await EseguiBackupLocaleAsync(MotivoBackup.PrimaDiRipristino, ct);
        if (!preventivo.Riuscito)
            return new EsitoBackup(false, "Ripristino annullato: non è stato possibile salvare lo stato attuale. " + preventivo.Messaggio);

        await Blocco.WaitAsync(ct);
        try
        {
            SqliteConnection.ClearAllPools();
            RipristinaSuDatabaseInUso(fileBackup, percorsi.StringaConnessione);
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            Blocco.Release();
        }

        await migrazioni.ApplicaAsync(ct);

        await using (var db = await fabbrica.CreateDbContextAsync(ct))
        {
            db.RegistraOperazione("Ripristino", "Database", null, null,
                $"Ripristinato il backup {Path.GetFileName(fileBackup)}. Stato precedente salvato in {Path.GetFileName(preventivo.Percorso)}.");
            await db.SaveChangesAsync(ct);
        }
        await RegistraAsync(TipoBackup.Ripristino, Path.GetFileName(fileBackup), true, fileBackup, null, null, null,
            "Stato precedente salvato in " + preventivo.Percorso, ct);

        log.LogWarning("Database ripristinato dal backup {File}", fileBackup);
        return new EsitoBackup(true, "Ripristino completato. Lo stato precedente è stato salvato in " + Path.GetFileName(preventivo.Percorso) + ".",
            preventivo.Percorso);
    }

    // ------------------------------------------------------------------ disco esterno

    public async Task<IReadOnlyList<DiscoEsterno>> DischiRegistratiAsync(CancellationToken ct = default)
    {
        var json = await impostazioni.LeggiAsync(ImpostazioniService.DischiEsterni, ct);
        return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<DiscoEsterno>>(json) ?? [];
    }

    /// <summary>Registra un disco esterno scrivendo nella sua radice un file di riconoscimento.</summary>
    public async Task<DiscoEsterno> RegistraDiscoAsync(string radiceDisco, string nome, CancellationToken ct = default)
    {
        if (!Directory.Exists(radiceDisco))
            throw new OperazioneNonValidaException("Il percorso indicato non esiste. Verifica che il disco sia collegato.");
        if (string.IsNullOrWhiteSpace(nome))
            throw new OperazioneNonValidaException("Indica un nome per il disco (es. \"Disco A\").");

        var disco = new DiscoEsterno(Guid.NewGuid(), nome.Trim(), orologio.GetUtcNow().UtcDateTime);
        await File.WriteAllTextAsync(Path.Combine(radiceDisco, FileIdentitaDisco), JsonSerializer.Serialize(disco), ct);

        var dischi = (await DischiRegistratiAsync(ct)).ToList();
        dischi.Add(disco);
        await impostazioni.ScriviAsync(ImpostazioniService.DischiEsterni, JsonSerializer.Serialize(dischi), ct);
        return disco;
    }

    /// <summary>Riconosce un disco registrato dal file presente nella sua radice.</summary>
    public async Task<DiscoEsterno?> RiconosciDiscoAsync(string radiceDisco, CancellationToken ct = default)
    {
        var file = Path.Combine(radiceDisco, FileIdentitaDisco);
        if (!File.Exists(file)) return null;
        try
        {
            var letto = JsonSerializer.Deserialize<DiscoEsterno>(await File.ReadAllTextAsync(file, ct));
            return (await DischiRegistratiAsync(ct)).FirstOrDefault(d => d.Id == letto?.Id);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Backup completo su disco esterno (sez. 6.3): database, documenti, configurazione e codice sorgente,
    /// con verifica delle impronte dopo la copia.
    /// </summary>
    public async Task<EsitoBackup> EseguiBackupEsternoAsync(string radiceDisco, CancellationToken ct = default)
    {
        var disco = await RiconosciDiscoAsync(radiceDisco, ct);
        if (disco is null)
            return new EsitoBackup(false, "Il disco non è registrato. Registralo prima di eseguire il backup esterno.");

        await Blocco.WaitAsync(ct);
        try
        {
            var radice = Path.Combine(radiceDisco, CartellaBackupEsterno);
            var cartellaDb = Path.Combine(radice, "Database");
            Directory.CreateDirectory(cartellaDb);

            // Database: copia coerente diretta sul disco, poi verifica.
            var adesso = orologio.GetLocalNow().DateTime;
            var fileDb = Path.Combine(cartellaDb, NomeFile(adesso, "Esterno"));
            CopiaDatabase(percorsi.StringaConnessione, fileDb);
            VerificaIntegrita(fileDb);

            // Documenti: copia incrementale e verifica delle impronte registrate.
            var copiati = CopiaIncrementale(percorsi.Documenti, Path.Combine(radice, "Documenti"));
            var errori = await VerificaDocumentiCopiatiAsync(Path.Combine(radice, "Documenti"), ct);
            if (errori.Count > 0)
                throw new IOException($"{errori.Count} documenti copiati non corrispondono all'originale (es. {errori[0]}).");

            // Configurazione e codice sorgente.
            CopiaIncrementale(percorsi.Config, Path.Combine(radice, "Config"), sovrascriviModificati: true);
            var codice = opzioni.Value.CartellaCodiceSorgente;
            if (!string.IsNullOrWhiteSpace(codice) && Directory.Exists(codice))
                CopiaIncrementale(codice, Path.Combine(radice, "CodiceSorgente"), sovrascriviModificati: true, escludiCartelleBuild: true);

            // Conservazione: stessa politica del backup locale.
            foreach (var vecchio in PoliticaConservazione.DaEliminare(ElencoBackup(cartellaDb), b => b.DataOra, adesso))
                File.Delete(vecchio.Percorso);

            var impronta = await DocumentiService.CalcolaImprontaAsync(fileDb, ct);
            await impostazioni.ScriviAsync(ImpostazioniService.UltimoBackupEsterno,
                orologio.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture), ct);
            await RegistraAsync(TipoBackup.Esterno, disco.Nome, true, fileDb, new FileInfo(fileDb).Length, impronta, copiati, null, ct);

            log.LogInformation("Backup esterno su {Disco} completato", disco.Nome);
            return new EsitoBackup(true,
                $"Backup su \"{disco.Nome}\" completato e verificato ({copiati} nuovi documenti copiati). Ora puoi scollegare il disco.",
                fileDb, copiati);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Backup esterno non riuscito");
            await RegistraSenzaErroriAsync(TipoBackup.Esterno, disco.Nome, ex.Message);
            return new EsitoBackup(false, "Backup esterno non riuscito: " + ex.Message);
        }
        finally
        {
            Blocco.Release();
        }
    }

    public async Task<DateTime?> UltimoBackupEsternoAsync(CancellationToken ct = default)
    {
        var valore = await impostazioni.LeggiAsync(ImpostazioniService.UltimoBackupEsterno, ct);
        return DateTime.TryParse(valore, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
    }

    // ------------------------------------------------------------------ utilità

    internal static string NomeFile(DateTime dataOra, string motivo) =>
        $"{PrefissoFile}{dataOra.ToString(FormatoData, CultureInfo.InvariantCulture)}_{motivo}.db";

    internal static IReadOnlyList<FileBackup> ElencoBackup(string cartella)
    {
        if (!Directory.Exists(cartella)) return [];
        var risultato = new List<FileBackup>();
        foreach (var file in Directory.EnumerateFiles(cartella, PrefissoFile + "*.db"))
        {
            var nome = Path.GetFileNameWithoutExtension(file);
            var parti = nome[PrefissoFile.Length..].Split('_', 3);
            if (parti.Length < 3) continue;
            if (!DateTime.TryParseExact(parti[0] + "_" + parti[1], FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                continue;
            risultato.Add(new FileBackup(file, Path.GetFileName(file), data, parti[2], new FileInfo(file).Length));
        }
        return risultato.OrderByDescending(f => f.DataOra).ToList();
    }

    /// <summary>
    /// Copia coerente con l'API di backup di SQLite. Le copie di backup sono in modalità DELETE
    /// (file unico autosufficiente); il database in uso torna in modalità WAL.
    /// </summary>
    private static void CopiaDatabase(string stringaConnessioneOrigine, string fileDestinazione, string modalitaJournal = "DELETE")
    {
        var temporaneo = fileDestinazione + ".tmp";
        if (File.Exists(temporaneo)) File.Delete(temporaneo);
        using (var origine = new SqliteConnection(stringaConnessioneOrigine))
        using (var destinazione = new SqliteConnection($"Data Source={temporaneo};Pooling=False"))
        {
            origine.Open();
            destinazione.Open();
            origine.BackupDatabase(destinazione);
            using var cmd = destinazione.CreateCommand();
            cmd.CommandText = $"PRAGMA journal_mode={modalitaJournal};";
            cmd.ExecuteNonQuery();
        }
        File.Move(temporaneo, fileDestinazione, overwrite: true);
    }

    private static void RipristinaSuDatabaseInUso(string fileBackup, string stringaConnessioneDatabase)
    {
        using var origine = new SqliteConnection($"Data Source={fileBackup};Mode=ReadOnly;Pooling=False");
        using var destinazione = new SqliteConnection(stringaConnessioneDatabase + ";Pooling=False");
        origine.Open();
        destinazione.Open();
        origine.BackupDatabase(destinazione);
        using var cmd = destinazione.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
    }

    internal static void VerificaIntegrita(string fileDatabase)
    {
        using var conn = new SqliteConnection($"Data Source={fileDatabase};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var esito = cmd.ExecuteScalar() as string;
        if (!string.Equals(esito, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Verifica di integrità del database non superata: " + esito);
    }

    /// <summary>Copia i file mancanti (o modificati, se richiesto) mantenendo la struttura delle cartelle.</summary>
    internal static int CopiaIncrementale(string origine, string destinazione, bool sovrascriviModificati = false, bool escludiCartelleBuild = false)
    {
        if (!Directory.Exists(origine)) return 0;
        var copiati = 0;
        foreach (var file in Directory.EnumerateFiles(origine, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(origine, file);
            var segmenti = relativo.Split(Path.DirectorySeparatorChar);
            if (segmenti.Contains("_temp")) continue;
            if (escludiCartelleBuild && segmenti.Any(s => s is "bin" or "obj" or ".vs" or "TestResults")) continue;

            var target = Path.Combine(destinazione, relativo);
            var info = new FileInfo(file);
            if (File.Exists(target))
            {
                if (!sovrascriviModificati) continue;
                var t = new FileInfo(target);
                if (t.Length == info.Length && t.LastWriteTimeUtc >= info.LastWriteTimeUtc) continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            copiati++;
        }
        return copiati;
    }

    private async Task<List<string>> VerificaDocumentiCopiatiAsync(string cartellaCopia, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var documenti = await db.Documenti.AsNoTracking().Select(d => new { d.PercorsoInterno, d.Impronta, d.NomeOriginale }).ToListAsync(ct);
        var errori = new List<string>();
        foreach (var d in documenti)
        {
            var copia = Path.Combine(cartellaCopia, d.PercorsoInterno);
            if (!File.Exists(copia) || await DocumentiService.CalcolaImprontaAsync(copia, ct) != d.Impronta)
                errori.Add(d.NomeOriginale);
        }
        return errori;
    }

    private void ApplicaConservazione()
    {
        var adesso = orologio.GetLocalNow().DateTime;
        foreach (var vecchio in PoliticaConservazione.DaEliminare(ElencoBackupLocali(), b => b.DataOra, adesso))
        {
            try { File.Delete(vecchio.Percorso); }
            catch (IOException ex) { log.LogWarning(ex, "Impossibile eliminare il backup scaduto {File}", vecchio.Percorso); }
        }
    }

    private async Task RegistraAsync(TipoBackup tipo, string motivo, bool riuscito, string? percorso, long? dimensione,
        string? impronta, int? documenti, string? messaggio, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        db.RegistroBackup.Add(new RegistroBackup
        {
            DataOra = orologio.GetUtcNow().UtcDateTime, Tipo = tipo, Motivo = motivo, Riuscito = riuscito,
            Percorso = percorso, Dimensione = dimensione, Impronta = impronta, DocumentiCopiati = documenti,
            Messaggio = messaggio, Utente = utente.Nome,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task RegistraSenzaErroriAsync(TipoBackup tipo, string motivo, string messaggio)
    {
        try { await RegistraAsync(tipo, motivo, false, null, null, null, null, messaggio, CancellationToken.None); }
        catch (Exception ex) { log.LogError(ex, "Impossibile registrare l'esito del backup"); }
    }
}
