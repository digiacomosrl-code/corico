using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Documenti.Dati;
using Corico.Modules.Documenti.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Documenti.Servizi;

public sealed record FiltroRicerca(string Testo, Guid? CommessaId = null, string? Tipo = null, DateOnly? Dal = null, DateOnly? Al = null);

public sealed record PaginaRisultato(int Numero, string Estratto, bool DaOcr);

public sealed record DocumentoTrovato(DocumentoRiepilogo Documento, IReadOnlyList<PaginaRisultato> Pagine);

public sealed record StatoLettura(int Letti, int InCoda, IReadOnlyList<LetturaInCorso> InCorso, bool OcrDisponibile,
    IReadOnlyList<(DocumentoRiepilogo Documento, TestoDocumento Testo)> DaVerificare);

public sealed record PropostaTipo(DocumentoRiepilogo Documento, string Tipo, string Motivo);

public sealed record CoppiaSimile(DocumentoRiepilogo A, DocumentoRiepilogo B, double Somiglianza);

/// <summary>Ricerca nel contenuto, tipi proposti e duplicati simili dell'archivio (specifica Modulo 3, sez. 4).</summary>
public sealed class RicercaDocumentiService(
    IDbContextFactory<DocumentiDbContext> fabbrica,
    DocumentiService documenti,
    IndiceRicerca indice,
    LetturaArchivioService lettura)
{
    /// <summary>Pagine esaminate al massimo per ricerca; i risultati si mostrano per documento.</summary>
    private const int MassimoPagine = 500;
    private const int PaginePerDocumento = 3;

    public async Task<IReadOnlyList<DocumentoTrovato>> CercaAsync(FiltroRicerca filtro, CancellationToken ct = default)
    {
        var pagine = await indice.CercaAsync(filtro.Testo, MassimoPagine, ct);
        if (pagine.Count == 0) return [];
        var archivio = await ArchivioAsync(filtro.CommessaId, ct);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var ocr = (await db.Pagine.AsNoTracking().Where(p => p.DaOcr)
            .Join(db.Testi, p => p.TestoDocumentoId, t => t.Id, (p, t) => new { t.DocumentoId, p.Numero })
            .ToListAsync(ct)).Select(x => (x.DocumentoId, x.Numero)).ToHashSet();

        return pagine
            .Where(p => archivio.ContainsKey(p.DocumentoId))
            .GroupBy(p => p.DocumentoId)
            .Select(g => (Doc: archivio[g.Key], Pagine: g.ToList()))
            .Where(x => Accetta(x.Doc, filtro))
            .OrderByDescending(x => x.Pagine.Max(p => p.Rilevanza))
            .Select(x => new DocumentoTrovato(x.Doc, x.Pagine.OrderBy(p => p.Pagina).Take(PaginePerDocumento)
                .Select(p => new PaginaRisultato(p.Pagina, p.Estratto, ocr.Contains((p.DocumentoId, p.Pagina)))).ToList()))
            .ToList();
    }

    public async Task<StatoLettura> StatoAsync(Guid? commessaId = null, CancellationToken ct = default)
    {
        var archivio = await ArchivioAsync(commessaId, ct);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var testi = await db.Testi.AsNoTracking().ToListAsync(ct);
        var letti = testi.Where(t => archivio.ContainsKey(t.DocumentoId)).ToList();
        var inCoda = (await lettura.InCodaAsync(ct)).Count(archivio.ContainsKey);
        var daVerificare = letti.Where(t => t.Esito is EsitoLettura.Errore or EsitoLettura.Parziale)
            .Select(t => (archivio[t.DocumentoId], t)).ToList();
        return new(letti.Count, inCoda, lettura.InCorso.Where(l => archivio.ContainsKey(l.DocumentoId)).ToList(),
            lettura.OcrDisponibile, daVerificare);
    }

    /// <summary>Tipi proposti non ancora esaminati e diversi da quello già indicato sul documento.</summary>
    public async Task<IReadOnlyList<PropostaTipo>> ProposteTipoAsync(Guid? commessaId = null, CancellationToken ct = default)
    {
        var archivio = await ArchivioAsync(commessaId, ct);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var testi = await db.Testi.AsNoTracking().Where(t => t.TipoProposto != null && !t.TipoEsaminato).ToListAsync(ct);
        return testi.Where(t => archivio.TryGetValue(t.DocumentoId, out var d) && !d.Archiviato
                && !string.Equals(d.Tipo, t.TipoProposto, StringComparison.OrdinalIgnoreCase))
            .Select(t => new PropostaTipo(archivio[t.DocumentoId], t.TipoProposto!, t.MotivoTipo ?? ""))
            .OrderByDescending(p => p.Documento.CaricatoIl)
            .ToList();
    }

    /// <summary>Conferma (il tipo passa sul documento) o scarta la proposta; in entrambi i casi non viene più mostrata.</summary>
    public async Task EsaminaTipoAsync(Guid documentoId, bool conferma, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var testo = await db.Testi.FirstOrDefaultAsync(t => t.DocumentoId == documentoId, ct)
            ?? throw new OperazioneNonValidaException("Il documento non è ancora stato letto.");
        if (conferma && testo.TipoProposto is { } tipo)
        {
            var d = (await ArchivioAsync(null, ct)).GetValueOrDefault(documentoId)
                ?? throw new OperazioneNonValidaException("Il documento non esiste.");
            await documenti.AggiornaMetadatiAsync(documentoId, tipo, d.Descrizione, d.DataDocumento, d.CommessaId, ct);
        }
        testo.TipoEsaminato = true;
        db.NotaStorico = conferma ? "Tipo proposto confermato" : "Tipo proposto scartato";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Coppie di documenti con testo quasi identico (es. stessa offerta rimandata o scansionata due volte).</summary>
    public async Task<IReadOnlyList<CoppiaSimile>> DuplicatiSimiliAsync(Guid? commessaId = null, CancellationToken ct = default)
    {
        var archivio = await ArchivioAsync(null, ct);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var firme = (await db.Testi.AsNoTracking().Where(t => t.Firma != null).Select(t => new { t.DocumentoId, t.Firma }).ToListAsync(ct))
            .Where(f => archivio.TryGetValue(f.DocumentoId, out var d) && !d.Archiviato)
            .ToList();
        var coppie = new List<CoppiaSimile>();
        for (var i = 0; i < firme.Count; i++)
            for (var j = i + 1; j < firme.Count; j++)
            {
                var (a, b) = (archivio[firme[i].DocumentoId], archivio[firme[j].DocumentoId]);
                if (commessaId is not null && a.CommessaId != commessaId && b.CommessaId != commessaId) continue;
                var s = Somiglianza.Confronta(firme[i].Firma!, firme[j].Firma!);
                if (s >= Somiglianza.Soglia) coppie.Add(a.CaricatoIl <= b.CaricatoIl ? new(a, b, s) : new(b, a, s));
            }
        return coppie.OrderByDescending(c => c.B.CaricatoIl).ToList();
    }

    /// <summary>Testo completo di un documento letto, pagina per pagina.</summary>
    public async Task<TestoDocumento?> TestoAsync(Guid documentoId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Testi.AsNoTracking().Include(t => t.Pagine.OrderBy(p => p.Numero)).FirstOrDefaultAsync(t => t.DocumentoId == documentoId, ct);
    }

    /// <summary>Allegati salvati a partire da una mail.</summary>
    public async Task<IReadOnlyList<AllegatoMail>> AllegatiAsync(Guid mailId, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.AllegatiMail.AsNoTracking().Where(a => a.MailDocumentoId == mailId).ToListAsync(ct);
    }

    private async Task<Dictionary<Guid, DocumentoRiepilogo>> ArchivioAsync(Guid? commessaId, CancellationToken ct) =>
        (await documenti.ElencoAsync(commessaId, includiArchiviati: true, ct: ct)).ToDictionary(d => d.Id);

    private static bool Accetta(DocumentoRiepilogo d, FiltroRicerca f)
    {
        if (f.Tipo is { Length: > 0 } tipo && !string.Equals(d.Tipo, tipo, StringComparison.OrdinalIgnoreCase)) return false;
        var data = d.DataDocumento ?? DateOnly.FromDateTime(d.CaricatoIl.ToLocalTime());
        return (f.Dal is null || data >= f.Dal) && (f.Al is null || data <= f.Al);
    }
}
