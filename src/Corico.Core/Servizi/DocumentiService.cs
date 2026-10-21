using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed record DocumentoRiepilogo(
    Guid Id, string NomeOriginale, string? Tipo, string? Descrizione, long Dimensione,
    DateOnly? DataDocumento, DateTime CaricatoIl, string CaricatoDa, Guid? CommessaId, string? CodiceCommessa, bool Archiviato);

public sealed record EsitoCaricamento(DocumentoRiepilogo Documento, bool GiaPresente);

public sealed record ProblemaCoerenza(string Descrizione, Guid? DocumentoId, string? Percorso);

/// <summary>
/// Archivio documentale (sez. 18): i file sono salvati con nomi interni nella cartella Documenti,
/// i metadati nel database. I duplicati esatti sono riconosciuti tramite impronta SHA-256.
/// </summary>
public sealed class DocumentiService(IDbContextFactory<CoreDbContext> fabbrica, PercorsiDati percorsi, TimeProvider orologio)
{
    public async Task<EsitoCaricamento> CaricaAsync(Stream contenuto, string nomeOriginale, string? tipoContenuto,
        Guid? commessaId, string? tipo = null, CancellationToken ct = default)
    {
        var nome = Path.GetFileName(nomeOriginale);
        if (string.IsNullOrWhiteSpace(nome))
            throw new OperazioneNonValidaException("Il nome del file non è valido.");

        // 1. Copia in un file temporaneo calcolando l'impronta.
        var cartellaTemp = Path.Combine(percorsi.Documenti, "_temp");
        Directory.CreateDirectory(cartellaTemp);
        var fileTemp = Path.Combine(cartellaTemp, Guid.NewGuid().ToString("N"));
        string impronta;
        long dimensione;
        try
        {
            await using (var destinazione = File.Create(fileTemp))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int letti;
                while ((letti = await contenuto.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, letti);
                    await destinazione.WriteAsync(buffer.AsMemory(0, letti), ct);
                }
                impronta = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                dimensione = destinazione.Length;
            }

            await using var db = await fabbrica.CreateDbContextAsync(ct);

            // 2. Duplicato esatto: non si salva una seconda copia.
            var esistente = await db.Documenti.AsNoTracking().Include(d => d.Commessa)
                .FirstOrDefaultAsync(d => d.Impronta == impronta, ct);
            if (esistente is not null)
                return new EsitoCaricamento(Riepilogo(esistente), GiaPresente: true);

            // 3. Spostamento nella posizione definitiva con nome interno.
            var adesso = orologio.GetLocalNow();
            var documento = new Documento
            {
                NomeOriginale = nome,
                Estensione = Path.GetExtension(nome).ToLowerInvariant(),
                TipoContenuto = string.IsNullOrWhiteSpace(tipoContenuto) ? "application/octet-stream" : tipoContenuto,
                Dimensione = dimensione,
                Impronta = impronta,
                CommessaId = commessaId,
                Tipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim(),
            };
            documento.PercorsoInterno = Path.Combine(adesso.ToString("yyyy", CultureInfo.InvariantCulture),
                adesso.ToString("MM", CultureInfo.InvariantCulture), documento.Id.ToString("N") + documento.Estensione);

            var percorsoFinale = Path.Combine(percorsi.Documenti, documento.PercorsoInterno);
            Directory.CreateDirectory(Path.GetDirectoryName(percorsoFinale)!);
            File.Move(fileTemp, percorsoFinale);

            db.Documenti.Add(documento);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                File.Delete(percorsoFinale);
                throw;
            }

            if (commessaId is not null)
                await db.Entry(documento).Reference(d => d.Commessa).LoadAsync(ct);
            return new EsitoCaricamento(Riepilogo(documento), GiaPresente: false);
        }
        finally
        {
            if (File.Exists(fileTemp)) File.Delete(fileTemp);
        }
    }

    public async Task<IReadOnlyList<DocumentoRiepilogo>> ElencoAsync(Guid? commessaId = null, string? testo = null,
        bool includiArchiviati = false, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var query = db.Documenti.AsNoTracking().Include(d => d.Commessa).AsQueryable();
        if (commessaId is not null) query = query.Where(d => d.CommessaId == commessaId);
        if (!includiArchiviati) query = query.Where(d => !d.Archiviato);
        if (!string.IsNullOrWhiteSpace(testo))
        {
            var t = testo.Trim().ToLower();
            query = query.Where(d => d.NomeOriginale.ToLower().Contains(t)
                || (d.Tipo != null && d.Tipo.ToLower().Contains(t))
                || (d.Descrizione != null && d.Descrizione.ToLower().Contains(t)));
        }
        var elenco = await query.OrderByDescending(d => d.CreatoIl).ToListAsync(ct);
        return elenco.Select(Riepilogo).ToList();
    }

    /// <summary>Apre il file in lettura. Restituisce null se il documento non esiste.</summary>
    public async Task<(Stream Contenuto, string Nome, string TipoContenuto)?> ApriAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var d = await db.Documenti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return null;
        var percorso = Path.Combine(percorsi.Documenti, d.PercorsoInterno);
        if (!File.Exists(percorso)) return null;
        return (File.OpenRead(percorso), d.NomeOriginale, d.TipoContenuto);
    }

    public async Task AggiornaMetadatiAsync(Guid id, string? tipo, string? descrizione, DateOnly? dataDocumento, Guid? commessaId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var d = await db.Documenti.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("Il documento non esiste.");
        d.Tipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim();
        d.Descrizione = string.IsNullOrWhiteSpace(descrizione) ? null : descrizione.Trim();
        d.DataDocumento = dataDocumento;
        d.CommessaId = commessaId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Archiviazione reversibile: il file non viene mai cancellato (sez. 22).</summary>
    public async Task ImpostaArchiviazioneAsync(Guid id, bool archiviato, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var d = await db.Documenti.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("Il documento non esiste.");
        d.Archiviato = archiviato;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Verifica che ogni documento registrato esista su disco con la stessa impronta
    /// e segnala i file presenti in archivio ma non registrati.
    /// </summary>
    public async Task<IReadOnlyList<ProblemaCoerenza>> VerificaCoerenzaAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var documenti = await db.Documenti.AsNoTracking().ToListAsync(ct);
        var problemi = new List<ProblemaCoerenza>();
        var registrati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in documenti)
        {
            var percorso = Path.Combine(percorsi.Documenti, d.PercorsoInterno);
            registrati.Add(Path.GetFullPath(percorso));
            if (!File.Exists(percorso))
            {
                problemi.Add(new($"File mancante: {d.NomeOriginale}", d.Id, d.PercorsoInterno));
                continue;
            }
            if (await CalcolaImprontaAsync(percorso, ct) != d.Impronta)
                problemi.Add(new($"Il contenuto del file non corrisponde all'impronta registrata: {d.NomeOriginale}", d.Id, d.PercorsoInterno));
        }

        if (Directory.Exists(percorsi.Documenti))
        {
            foreach (var file in Directory.EnumerateFiles(percorsi.Documenti, "*", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "_temp" + Path.DirectorySeparatorChar)) continue;
                if (!registrati.Contains(Path.GetFullPath(file)))
                    problemi.Add(new("File presente in archivio ma non registrato", null, Path.GetRelativePath(percorsi.Documenti, file)));
            }
        }
        return problemi;
    }

    /// <summary>Indice leggibile dell'archivio in formato CSV (separatore ";" per Excel italiano).</summary>
    public async Task<string> IndiceCsvAsync(CancellationToken ct = default)
    {
        var elenco = await ElencoAsync(includiArchiviati: true, ct: ct);
        var sb = new StringBuilder();
        sb.AppendLine("Commessa;Nome file;Tipo;Descrizione;Data documento;Caricato il;Caricato da;Dimensione (byte);Archiviato;Identificativo");
        foreach (var d in elenco)
        {
            sb.AppendJoin(';', Csv(d.CodiceCommessa), Csv(d.NomeOriginale), Csv(d.Tipo), Csv(d.Descrizione),
                Csv(d.DataDocumento?.ToString("dd/MM/yyyy")), Csv(d.CaricatoIl.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                Csv(d.CaricatoDa), d.Dimensione, d.Archiviato ? "Sì" : "No", d.Id);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static async Task<string> CalcolaImprontaAsync(string percorso, CancellationToken ct = default)
    {
        await using var f = File.OpenRead(percorso);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
    }

    private static string Csv(string? s) =>
        s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static DocumentoRiepilogo Riepilogo(Documento d) => new(
        d.Id, d.NomeOriginale, d.Tipo, d.Descrizione, d.Dimensione, d.DataDocumento,
        DateTime.SpecifyKind(d.CreatoIl, DateTimeKind.Utc), d.CreatoDa, d.CommessaId, d.Commessa?.Codice, d.Archiviato);
}
