using Corico.Core.Calcolo;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Esportazioni;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record FileEsportato(byte[] Contenuto, string TipoContenuto, string Nome);

/// <summary>Esportazioni del preventivo: Excel del computo e del riepilogo, PDF A4 del riepilogo e delle schede pacchetto.</summary>
public sealed class EsportazioniService(
    RiepilogoService riepilogo, VersioniService versioni, PacchettiService pacchetti, ListaSpesaService listaSpesa,
    OrdiniService ordini, RichiesteService richieste, QualificheService qualifiche, DocumentiService documenti, CommesseService commesse,
    ImpostazioniService impostazioni, TimeProvider orologio)
{
    public const string TipoExcel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string TipoPdf = "application/pdf";

    public async Task<FileEsportato?> ComputoExcelAsync(Guid commessaId, Guid? versioneId, CancellationToken ct = default)
    {
        var dati = await DatiAsync(commessaId, versioneId, ct);
        return dati is null ? null
            : new(EsportazioneExcel.Computo(dati.Value.Intestazione, dati.Value.Fotografia), TipoExcel, Nome("Computo", dati.Value, "xlsx"));
    }

    public async Task<FileEsportato?> RiepilogoExcelAsync(Guid commessaId, Guid? versioneId, CancellationToken ct = default)
    {
        var dati = await DatiAsync(commessaId, versioneId, ct);
        return dati is null ? null
            : new(EsportazioneExcel.Riepilogo(dati.Value.Intestazione, dati.Value.Fotografia), TipoExcel, Nome("Riepilogo", dati.Value, "xlsx"));
    }

    public async Task<FileEsportato?> RiepilogoPdfAsync(Guid commessaId, Guid? versioneId, CancellationToken ct = default)
    {
        var dati = await DatiAsync(commessaId, versioneId, ct);
        if (dati is null) return null;
        // Gli avvisi descrivono lo stato attuale: si stampano solo con la bozza.
        var avvisi = versioneId is null ? await riepilogo.AvvisiAsync(commessaId, ct) : null;
        return new(EsportazionePdf.Riepilogo(dati.Value.Intestazione, dati.Value.Fotografia, avvisi), TipoPdf, Nome("Riepilogo", dati.Value, "pdf"));
    }

    public async Task<FileEsportato?> ListaSpesaExcelAsync(Guid commessaId, string? fornitore = null, string? cerca = null, CancellationToken ct = default)
    {
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        if (commessa is null) return null;
        var lista = (await listaSpesa.CalcolaAsync(commessaId, ct)).Filtra(fornitore, cerca);
        var filtro = string.Join(", ", new[]
        {
            string.IsNullOrWhiteSpace(fornitore) ? null : $"fornitore {fornitore}",
            string.IsNullOrWhiteSpace(cerca) ? null : $"ricerca «{cerca.Trim()}»",
        }.Where(x => x is not null));
        var intestazione = new IntestazioneStampa(NomeCommessa(commessa),
            $"Bozza al {Formato.DataOra(orologio.GetUtcNow().UtcDateTime)} – offerte scelte" + (filtro.Length > 0 ? $" – filtro: {filtro}" : ""));
        return new(EsportazioneExcel.ListaSpesa(intestazione, lista), TipoExcel, $"Lista della Spesa {commessa.Codice} {orologio.GetLocalNow():yyyyMMdd}.xlsx");
    }

    public async Task<FileEsportato?> PacchettoPdfAsync(Guid commessaId, Guid pacchettoId, CancellationToken ct = default)
    {
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        var dettaglio = await pacchetti.DettaglioAsync(commessaId, pacchettoId, ct);
        if (commessa is null || dettaglio is null) return null;
        var intestazione = new IntestazioneStampa(NomeCommessa(commessa), $"Bozza al {Formato.DataOra(orologio.GetUtcNow().UtcDateTime)}");
        var nome = $"Pacchetto {dettaglio.Pacchetto.Ordine} {Pulisci(dettaglio.Pacchetto.Descrizione)} – {commessa.Codice}.pdf";
        return new(EsportazionePdf.Pacchetto(intestazione, dettaglio), TipoPdf, nome);
    }

    public async Task<FileEsportato?> OrdinePdfAsync(Guid commessaId, Guid ordineId, CancellationToken ct = default)
    {
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        var ordine = await ordini.OttieniAsync(commessaId, ordineId, ct);
        if (commessa is null || ordine is null) return null;
        var azienda = await impostazioni.LeggiAziendaAsync(ct);
        if (ordine.Stato == Dominio.StatoOrdine.Bozza) ordine.Condizioni = azienda.CondizioniOrdine;   // anteprima
        var nome = ordine.Stato == Dominio.StatoOrdine.Bozza
            ? $"Bozza ordine {Pulisci(ordine.Fornitore)}.pdf"
            : $"{ordine.Numero.Replace('/', '-')} {Pulisci(ordine.Fornitore)}{(ordine.Stato == Dominio.StatoOrdine.Annullato ? " ANNULLATO" : "")}.pdf";
        return new(EsportazionePdf.Ordine(azienda, ordine, NomeCommessa(commessa)), TipoPdf, nome);
    }

    /// <summary>
    /// Comunicazione di qualificazione: la bozza si compone al momento (anteprima); dal PDF definitivo si restituisce
    /// sempre il file archiviato, identico a quello generato.
    /// </summary>
    public async Task<FileEsportato?> ComunicazioneQualificaAsync(Guid commessaId, Guid comunicazioneId, CancellationToken ct = default)
    {
        var l = await qualifiche.LetteraAsync(commessaId, comunicazioneId, ct);
        if (l is null) return null;
        if (l.Comunicazione.DocumentoId is { } id && await documenti.ApriAsync(id, ct) is { } archiviato)
        {
            await using var flusso = archiviato.Contenuto;
            using var ms = new MemoryStream();
            await flusso.CopyToAsync(ms, ct);
            return new(ms.ToArray(), TipoPdf, archiviato.Nome);
        }
        return new(EsportazionePdf.ComunicazioneQualifica(l), TipoPdf, $"Bozza comunicazione qualificazione {Pulisci(l.Qualifica.Fornitore)}.pdf");
    }

    public async Task<FileEsportato?> RichiestaAsync(Guid commessaId, Guid richiestaId, Guid? destinatarioId, bool excel, CancellationToken ct = default)
    {
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        var r = await richieste.OttieniAsync(commessaId, richiestaId, ct);
        if (commessa is null || r is null) return null;
        var fornitore = destinatarioId is null ? null : r.Destinatari.FirstOrDefault(d => d.Id == destinatarioId)?.Fornitore;
        var azienda = await impostazioni.LeggiAziendaAsync(ct);
        var nome = $"{r.Numero.Replace('/', '-')}{(fornitore is null ? "" : " " + Pulisci(fornitore))}";
        return excel
            ? new(EsportazioneExcel.Richiesta(azienda, r, fornitore, NomeCommessa(commessa)), TipoExcel, nome + ".xlsx")
            : new(EsportazionePdf.Richiesta(azienda, r, fornitore, NomeCommessa(commessa)), TipoPdf, nome + ".pdf");
    }

    private async Task<(IntestazioneStampa Intestazione, Fotografia Fotografia, string Riferimento, string Codice)?> DatiAsync(
        Guid commessaId, Guid? versioneId, CancellationToken ct)
    {
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        if (commessa is null) return null;
        if (versioneId is null)
        {
            var f = await riepilogo.FotografiaAsync(commessaId, ct);
            return (new(NomeCommessa(commessa), $"Bozza al {Formato.DataOra(f.DataUtc)} – non è una versione ufficiale"), f, "bozza", commessa.Codice);
        }
        var v = (await versioni.ElencoAsync(commessaId, ct)).FirstOrDefault(x => x.Id == versioneId);
        if (v is null) return null;
        return (new(NomeCommessa(commessa), $"{VersioniService.Etichetta(v)} – {v.Nota}"), VersioniService.Leggi(v), $"v{v.Numero}", commessa.Codice);
    }

    private static string NomeCommessa(DatiCommessa c) =>
        $"Commessa {c.Codice}{(string.IsNullOrWhiteSpace(c.CodiceEsterno) ? "" : $" (rif. {c.CodiceEsterno})")} – {c.Descrizione}";

    private string Nome(string tipo, (IntestazioneStampa, Fotografia, string Riferimento, string Codice) dati, string estensione) =>
        $"{tipo} {dati.Codice} {dati.Riferimento} {orologio.GetLocalNow():yyyyMMdd}.{estensione}";

    private static string Pulisci(string s)
    {
        var nonValidi = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var t = new string(s.Where(c => !nonValidi.Contains(c)).ToArray()).Trim();
        return t.Length > 60 ? t[..60].Trim() : t;
    }
}
