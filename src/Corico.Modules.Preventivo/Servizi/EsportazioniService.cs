using Corico.Core.Calcolo;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Esportazioni;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record FileEsportato(byte[] Contenuto, string TipoContenuto, string Nome);

/// <summary>Esportazioni del preventivo: Excel del computo e del riepilogo, PDF A4 del riepilogo e delle schede pacchetto.</summary>
public sealed class EsportazioniService(
    RiepilogoService riepilogo, VersioniService versioni, PacchettiService pacchetti, ListaSpesaService listaSpesa,
    CommesseService commesse, TimeProvider orologio)
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

    public async Task<FileEsportato?> ListaSpesaExcelAsync(Guid commessaId, CancellationToken ct = default)
    {
        var commessa = await commesse.OttieniAsync(commessaId, ct);
        if (commessa is null) return null;
        var lista = await listaSpesa.CalcolaAsync(commessaId, ct);
        var intestazione = new IntestazioneStampa(NomeCommessa(commessa), $"Bozza al {Formato.DataOra(orologio.GetUtcNow().UtcDateTime)} – offerte scelte");
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
