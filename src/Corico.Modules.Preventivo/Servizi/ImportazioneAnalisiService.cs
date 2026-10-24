using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corico.Core.Backup;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>File caricato dall'utente, letto in memoria.</summary>
public sealed record FileCaricato(string Nome, byte[] Contenuto);

public enum EsitoAnalisiDaFile
{
    Nuova,
    Aggiornata,
    Invariata,

    /// <summary>Modificata a mano in Corico dopo l'ultima importazione (o creata a mano con lo stesso codice): si conserva.</summary>
    ModificataInCorico,

    Errata,
}

public sealed record AnalisiInAnteprima(string File, int Riga, string? Codice, string Descrizione, string? Unita, StatoAnalisi Stato,
    int Risorse, decimal CostoUnitario, EsitoAnalisiDaFile Esito, string? Dettaglio);

public sealed record AnteprimaAnalisiDaFile(IReadOnlyList<string> File, IReadOnlyList<AnalisiInAnteprima> Analisi,
    IReadOnlyList<string> Avvisi, int VociEscluse)
{
    public int Conta(EsitoAnalisiDaFile esito) => Analisi.Count(a => a.Esito == esito);
    public int InBozza => Analisi.Count(a => a.Esito is EsitoAnalisiDaFile.Nuova or EsitoAnalisiDaFile.Aggiornata && a.Stato == StatoAnalisi.Bozza);
    public int DaSalvare => Conta(EsitoAnalisiDaFile.Nuova) + Conta(EsitoAnalisiDaFile.Aggiornata);
}

public sealed record EsitoImportazioneAnalisi(int Nuove, int Aggiornate, int Invariate, int Conservate, int Errate);

/// <summary>
/// «Importa analisi da file» (es. le Analisi costo SIC24): legge testate e risorse, mostra un'anteprima e salva in blocco.
/// Il Codice è la chiave: un'analisi già importata si aggiorna (testata e risorse) solo se in Corico nessuno l'ha modificata
/// dopo l'importazione; altrimenti si conserva com'è. Per riconoscerlo si registra l'impronta del contenuto importato.
/// Le voci d'offerta che hanno già applicato un'analisi non cambiano, perché ne hanno copiato le risorse.
/// </summary>
public sealed class ImportazioneAnalisiService(
    IDbContextFactory<PreventivoDbContext> fabbrica,
    ImpostazioniService impostazioni,
    BackupService backup)
{
    /// <summary>Impostazione con le impronte dell'ultima importazione: identificativo dell'analisi → impronta.</summary>
    public const string ChiaveImpronte = "Preventivo.AnalisiImportate.Impronte";

    private sealed record Confronto(AnalisiLetta Letta, AnalisiAziendale Pulita, string Impronta, AnalisiAziendale? Esistente,
        EsitoAnalisiDaFile Esito, string? Dettaglio);

    public async Task<AnteprimaAnalisiDaFile> AnteprimaAsync(IReadOnlyList<FileCaricato> file, CancellationToken ct = default)
    {
        var letto = Leggi(file);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var esistenti = await db.AnalisiAziendali.AsNoTracking().Include(a => a.Righe).Where(a => a.Codice != null).ToListAsync(ct);
        var confronti = Confronta(letto, esistenti, await LeggiImpronteAsync(ct));
        return Anteprima(file, letto, confronti);
    }

    /// <summary>Rilegge i file e salva le analisi nuove e aggiornate. Esegue prima il backup del database.</summary>
    public async Task<EsitoImportazioneAnalisi> ConfermaAsync(IReadOnlyList<FileCaricato> file, CancellationToken ct = default)
    {
        var letto = Leggi(file);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var esistenti = await db.AnalisiAziendali.Include(a => a.Righe).Where(a => a.Codice != null).ToListAsync(ct);
        var impronte = await LeggiImpronteAsync(ct);
        var confronti = Confronta(letto, esistenti, impronte);
        int Conta(EsitoAnalisiDaFile e) => confronti.Count(c => c.Esito == e);
        var esito = new EsitoImportazioneAnalisi(Conta(EsitoAnalisiDaFile.Nuova), Conta(EsitoAnalisiDaFile.Aggiornata),
            Conta(EsitoAnalisiDaFile.Invariata), Conta(EsitoAnalisiDaFile.ModificataInCorico), Conta(EsitoAnalisiDaFile.Errata));
        if (esito.Nuove + esito.Aggiornate == 0)
            throw new OperazioneNonValidaException("Non c'è nessuna analisi nuova o da aggiornare: il file corrisponde a quanto già presente in Corico.");

        var esitoBackup = await backup.EseguiBackupLocaleAsync(MotivoBackup.PrimaDiImportazione, ct);
        if (!esitoBackup.Riuscito)
            throw new OperazioneNonValidaException("Importazione annullata: il backup preventivo non è riuscito. " + esitoBackup.Messaggio);

        db.InserimentiSoloInRiepilogo = true;
        foreach (var c in confronti)
        {
            switch (c.Esito)
            {
                case EsitoAnalisiDaFile.Nuova:
                    var nuova = new AnalisiAziendale { Stato = c.Pulita.Stato };
                    AnalisiService.Applica(c.Pulita, nuova);
                    db.AnalisiAziendali.Add(nuova);
                    impronte[nuova.Id] = c.Impronta;
                    break;
                case EsitoAnalisiDaFile.Aggiornata:
                    var a = c.Esistente!;
                    db.RigheAnalisiAziendali.RemoveRange(a.Righe);
                    a.Righe.Clear();
                    a.Stato = c.Pulita.Stato;
                    AnalisiService.Applica(c.Pulita, a, db);
                    impronte[a.Id] = c.Impronta;
                    break;
                case EsitoAnalisiDaFile.Invariata:
                    impronte[c.Esistente!.Id] = c.Impronta;
                    break;
            }
        }
        var nomi = string.Join(", ", file.Select(f => $"«{f.Nome}»"));
        db.RegistraOperazione("Importazione", nameof(AnalisiAziendale), null, null,
            $"Analisi da {nomi}: {esito.Nuove} nuove, {esito.Aggiornate} aggiornate, {esito.Invariate} invariate, " +
            $"{esito.Conservate} conservate perché modificate in Corico, {esito.Errate} con errori");
        await db.SaveChangesAsync(ct);

        // Le impronte delle analisi eliminate nel frattempo non servono più.
        var presenti = await db.AnalisiAziendali.Select(x => x.Id).ToListAsync(ct);
        var valide = presenti.Where(impronte.ContainsKey).ToDictionary(id => id, id => impronte[id]);
        await impostazioni.ScriviAsync(ChiaveImpronte, JsonSerializer.Serialize(valide), ct);
        return esito;
    }

    private static FileAnalisiLetto Leggi(IReadOnlyList<FileCaricato> file)
    {
        if (file.Count == 0) throw new OperazioneNonValidaException("Scegli il file da importare.");
        var flussi = file.Select(f => (f.Nome, (Stream)new MemoryStream(f.Contenuto, writable: false))).ToList();
        try
        {
            return LettoreAnalisi.Leggi(flussi);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            throw new OperazioneNonValidaException("Il file non è un Excel (.xlsx) leggibile: " + ex.Message);
        }
        finally
        {
            foreach (var (_, s) in flussi) s.Dispose();
        }
    }

    private static List<Confronto> Confronta(FileAnalisiLetto letto, List<AnalisiAziendale> esistenti, Dictionary<Guid, string> impronte)
    {
        // A parità di codice prevale l'analisi già importata, poi quella non archiviata più recente.
        var perCodice = esistenti.GroupBy(a => a.Codice!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => impronte.ContainsKey(a.Id))
                .ThenBy(a => a.Stato == StatoAnalisi.Archiviata)
                .ThenByDescending(a => a.ModificatoIl ?? a.CreatoIl).First(), StringComparer.OrdinalIgnoreCase);

        var confronti = new List<Confronto>();
        foreach (var l in letto.Analisi)
        {
            var pulita = new AnalisiAziendale { Id = Guid.Empty, Stato = l.Analisi.Stato };
            AnalisiService.Applica(l.Analisi, pulita);
            var impronta = Impronta(pulita);
            if (!l.Valida)
            {
                confronti.Add(new Confronto(l, pulita, impronta, null, EsitoAnalisiDaFile.Errata, string.Join(" ", l.Errori)));
                continue;
            }
            if (!perCodice.TryGetValue(pulita.Codice!, out var esistente))
            {
                confronti.Add(new Confronto(l, pulita, impronta, null, EsitoAnalisiDaFile.Nuova, null));
                continue;
            }
            var attuale = Impronta(esistente);
            var (esito, dettaglio) = attuale == impronta
                ? (EsitoAnalisiDaFile.Invariata, (string?)null)
                : impronte.TryGetValue(esistente.Id, out var importata) && importata == attuale
                    ? (EsitoAnalisiDaFile.Aggiornata, Differenze(esistente, pulita))
                    : (EsitoAnalisiDaFile.ModificataInCorico, impronte.ContainsKey(esistente.Id)
                        ? "Modificata in Corico dopo l'importazione: si conserva la versione di Corico."
                        : "In Corico c'è già un'analisi con questo codice non importata da file: si conserva.");
            confronti.Add(new Confronto(l, pulita, impronta, esistente, esito, dettaglio));
        }
        return confronti;
    }

    private static AnteprimaAnalisiDaFile Anteprima(IReadOnlyList<FileCaricato> file, FileAnalisiLetto letto, List<Confronto> confronti) =>
        new(file.Select(f => f.Nome).ToList(),
            confronti.Select(c => new AnalisiInAnteprima(c.Letta.File, c.Letta.Riga, c.Pulita.Codice, c.Pulita.Descrizione, c.Pulita.Unita,
                c.Pulita.Stato, c.Pulita.Righe.Count, c.Pulita.Righe.Sum(Risorse.CostoUnitario), c.Esito, c.Dettaglio)).ToList(),
            letto.Avvisi, letto.VociEscluse);

    private static string Differenze(AnalisiAziendale prima, AnalisiAziendale dopo)
    {
        var costoPrima = prima.Righe.Sum(Risorse.CostoUnitario);
        var costoDopo = dopo.Righe.Sum(Risorse.CostoUnitario);
        var parti = new List<string>();
        if (prima.Righe.Count != dopo.Righe.Count) parti.Add($"risorse da {prima.Righe.Count} a {dopo.Righe.Count}");
        if (Math.Round(costoPrima, 2) != Math.Round(costoDopo, 2))
            parti.Add($"costo unitario da {costoPrima.ToString("N2", Cultura)} a {costoDopo.ToString("N2", Cultura)} €");
        if (prima.Stato != dopo.Stato) parti.Add($"stato da {prima.Stato} a {dopo.Stato}");
        return parti.Count == 0 ? "Testo o dettagli delle risorse aggiornati." : "Cambia: " + string.Join(", ", parti) + ".";
    }

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Impronta del contenuto: testata, stato e risorse nell'ordine; i numeri senza zeri finali.</summary>
    internal static string Impronta(AnalisiAziendale a)
    {
        var sb = new StringBuilder();
        void Campo(object? v) => sb.Append(v switch
        {
            decimal d => d.ToString("0.############################", CultureInfo.InvariantCulture),
            null => "",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture),
        }).Append('\u001f');
        Campo(a.Codice); Campo(a.Descrizione); Campo(a.Unita); Campo((int)a.Stato); Campo(a.Fonte); Campo(a.Note);
        foreach (var r in a.Righe.OrderBy(r => r.Ordine))
        {
            sb.Append('\u001e');
            Campo((int)r.Categoria); Campo(r.Descrizione); Campo(r.Unita); Campo(r.Fabbisogno); Campo(r.SfridoPercentuale);
            Campo(r.Prezzo); Campo(r.Fonte); Campo(r.Note);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())), 0, 12);
    }

    private async Task<Dictionary<Guid, string>> LeggiImpronteAsync(CancellationToken ct)
    {
        var testo = await impostazioni.LeggiAsync(ChiaveImpronte, ct);
        if (string.IsNullOrEmpty(testo)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, string>>(testo) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
