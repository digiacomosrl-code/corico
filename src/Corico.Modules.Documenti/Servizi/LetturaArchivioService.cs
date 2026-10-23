using System.Collections.Concurrent;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Servizi;
using Corico.Modules.Documenti.Dati;
using Corico.Modules.Documenti.Dominio;
using Corico.Modules.Documenti.Estrazione;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Corico.Modules.Documenti.Servizi;

public sealed record LetturaInCorso(Guid DocumentoId, string Nome, int PagineFatte, int PagineTotali);

/// <summary>
/// Legge il contenuto dei documenti dell'archivio, uno alla volta e in sottofondo (specifica Modulo 3, sez. 4):
/// testo, OCR delle scansioni, allegati delle mail, tipo proposto, firma per i duplicati simili e indice di ricerca.
/// Il documento resta consultabile da subito; la ricerca nel suo contenuto arriva a lettura finita.
/// </summary>
public sealed class LetturaArchivioService(
    IDbContextFactory<CoreDbContext> fabbricaCore,
    IDbContextFactory<DocumentiDbContext> fabbrica,
    DocumentiService documenti,
    LetturaDocumento lettura,
    IndiceRicerca indice,
    PercorsiDati percorsi,
    ILogger<LetturaArchivioService> log)
{
    private readonly ConcurrentDictionary<Guid, LetturaInCorso> _inCorso = new();
    private readonly SemaphoreSlim _uno = new(1, 1);

    public IReadOnlyCollection<LetturaInCorso> InCorso => _inCorso.Values.ToList();

    public bool OcrDisponibile => lettura.OcrDisponibile;

    /// <summary>Documenti dell'archivio non ancora letti, dal più vecchio.</summary>
    public async Task<IReadOnlyList<Guid>> InCodaAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var letti = (await db.Testi.AsNoTracking().Select(t => t.DocumentoId).ToListAsync(ct)).ToHashSet();
        await using var core = await fabbricaCore.CreateDbContextAsync(ct);
        var tutti = await core.Documenti.AsNoTracking().OrderBy(d => d.CreatoIl).Select(d => d.Id).ToListAsync(ct);
        return tutti.Where(id => !letti.Contains(id)).ToList();
    }

    /// <summary>Legge tutti i documenti in coda; restituisce quanti ne ha letti. Usata dalla coda in sottofondo e dalle prove.</summary>
    public async Task<int> LeggiInCodaAsync(CancellationToken ct = default)
    {
        var letti = 0;
        // Gli allegati delle mail entrano in coda durante la lettura: si ripete finché la coda è vuota.
        while (await InCodaAsync(ct) is { Count: > 0 } coda)
        {
            foreach (var id in coda)
            {
                ct.ThrowIfCancellationRequested();
                await LeggiAsync(id, ct);
                letti++;
            }
        }
        return letti;
    }

    /// <summary>Legge un documento, se non è già stato letto.</summary>
    public async Task LeggiAsync(Guid documentoId, CancellationToken ct = default)
    {
        await _uno.WaitAsync(ct);
        try
        {
            await using (var db = await fabbrica.CreateDbContextAsync(ct))
                if (await db.Testi.AnyAsync(t => t.DocumentoId == documentoId, ct)) return;

            await using var core = await fabbricaCore.CreateDbContextAsync(ct);
            var doc = await core.Documenti.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentoId, ct);
            if (doc is null) return;

            var testo = new TestoDocumento { DocumentoId = documentoId };
            var percorso = Path.Combine(percorsi.Documenti, doc.PercorsoInterno);
            _inCorso[documentoId] = new(documentoId, doc.NomeOriginale, 0, 0);
            try
            {
                if (!File.Exists(percorso)) throw new FileNotFoundException("Il file del documento non si trova nell'archivio.");
                var esito = await lettura.LeggiAsync(percorso, doc.Estensione,
                    (fatte, totali) => _inCorso[documentoId] = new(documentoId, doc.NomeOriginale, fatte, totali), ct);
                Compila(testo, esito);
                if (esito.Mail is { } mail) await SalvaAllegatiAsync(documentoId, doc.CommessaId, mail, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Lettura del documento {Nome} non riuscita", doc.NomeOriginale);
                testo.Esito = EsitoLettura.Errore;
                testo.Metodo = MetodoLettura.Nessuno;
                testo.Messaggio = "Lettura non riuscita: " + ex.Message;
                testo.Pagine.Clear();
            }
            finally
            {
                _inCorso.TryRemove(documentoId, out _);
            }

            await using (var db = await fabbrica.CreateDbContextAsync(ct))
            {
                // Dato tecnico: le pagine non vanno una per una nello storico delle modifiche.
                db.InserimentiSoloInRiepilogo = true;
                db.Testi.Add(testo);
                await db.SaveChangesAsync(ct);
            }
            await indice.IndicizzaAsync(documentoId, testo.Pagine.Select(p => (p.Numero, p.Testo)), ct);
        }
        finally
        {
            _uno.Release();
        }
    }

    /// <summary>Cancella la lettura di un documento (es. dopo un errore o dopo aver installato l'OCR) e lo rimette in coda.</summary>
    public async Task RileggiAsync(Guid documentoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var testo = await db.Testi.FirstOrDefaultAsync(t => t.DocumentoId == documentoId, ct);
        if (testo is null) return;
        db.Testi.Remove(testo);
        db.NotaStorico = "Documento rimesso in coda per una nuova lettura";
        await db.SaveChangesAsync(ct);
        await indice.RimuoviAsync(documentoId, ct);
    }

    /// <summary>Se l'indice di ricerca manca (es. database ripristinato da una versione senza indice), lo ricostruisce dal testo.</summary>
    public async Task RicostruisciIndiceSeVuotoAsync(CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        if (await indice.ContaPagineAsync(ct) > 0 || !await db.Pagine.AnyAsync(ct)) return;
        var testi = await db.Testi.AsNoTracking().Include(t => t.Pagine).ToListAsync(ct);
        foreach (var t in testi)
            await indice.IndicizzaAsync(t.DocumentoId, t.Pagine.Select(p => (p.Numero, p.Testo)), ct);
    }

    private static void Compila(TestoDocumento testo, EsitoEstrazione esito)
    {
        testo.Pagine = esito.Pagine.Select(p => new PaginaTesto { Numero = p.Numero, Testo = p.Testo, DaOcr = p.DaOcr }).ToList();
        testo.NumeroPagine = esito.Pagine.Count;
        testo.PagineOcr = esito.Pagine.Count(p => p.DaOcr);
        testo.Messaggio = esito.Messaggio;
        testo.Esito = !esito.Supportato ? EsitoLettura.NonSupportata
            : esito.PagineSenzaTesto > 0 ? EsitoLettura.Parziale
            : EsitoLettura.Completata;
        testo.Metodo = testo.PagineOcr == 0 ? (esito.Supportato ? MetodoLettura.Testo : MetodoLettura.Nessuno)
            : testo.PagineOcr == testo.NumeroPagine ? MetodoLettura.Ocr
            : MetodoLettura.Misto;

        var pagine = esito.Pagine.Select(p => p.Testo).ToList();
        if (Classificatore.Proponi(pagine, esito.Mail is not null) is { } tipo)
            (testo.TipoProposto, testo.MotivoTipo) = (tipo.Tipo, tipo.Motivo);
        testo.Firma = Somiglianza.Firma(string.Join("\n", pagine));

        if (esito.Mail is { } mail)
            (testo.MailMittente, testo.MailOggetto, testo.MailData) = (Taglia(mail.Mittente, 300), Taglia(mail.Oggetto, 500), mail.Data);
    }

    /// <summary>Gli allegati diventano documenti dell'archivio, nella stessa commessa della mail, e saranno letti a loro volta.</summary>
    private async Task SalvaAllegatiAsync(Guid mailId, Guid? commessaId, DatiMail mail, CancellationToken ct)
    {
        if (mail.Allegati.Count == 0) return;
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        foreach (var a in mail.Allegati)
        {
            using var contenuto = new MemoryStream(a.Contenuto);
            var esito = await documenti.CaricaAsync(contenuto, a.Nome, a.TipoContenuto, commessaId, ct: ct);
            db.AllegatiMail.Add(new AllegatoMail { MailDocumentoId = mailId, AllegatoDocumentoId = esito.Documento.Id, NomeFile = Taglia(a.Nome, 300)! });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string? Taglia(string? s, int n) => s is null || s.Length <= n ? s : s[..n];
}
