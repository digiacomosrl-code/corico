using System.Text.Json;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed class ImpostazioniService(IDbContextFactory<CoreDbContext> fabbrica, TimeProvider orologio)
{
    public const string UltimoBackupEsterno = "Backup.UltimoBackupEsterno";
    public const string DischiEsterni = "Backup.DischiEsterni";
    public const string Azienda = "Azienda.Dati";

    /// <summary>Dati dell'impresa per l'intestazione dei documenti (es. ordini di acquisto).</summary>
    public async Task<DatiAzienda> LeggiAziendaAsync(CancellationToken ct = default)
    {
        var json = await LeggiAsync(Azienda, ct);
        return string.IsNullOrEmpty(json) ? new DatiAzienda() : JsonSerializer.Deserialize<DatiAzienda>(json) ?? new DatiAzienda();
    }

    public Task SalvaAziendaAsync(DatiAzienda dati, CancellationToken ct = default) =>
        ScriviAsync(Azienda, JsonSerializer.Serialize(dati), ct);

    public async Task<string?> LeggiAsync(string chiave, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return (await db.Impostazioni.AsNoTracking().FirstOrDefaultAsync(i => i.Chiave == chiave, ct))?.Valore;
    }

    public async Task ScriviAsync(string chiave, string? valore, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var imp = await db.Impostazioni.FirstOrDefaultAsync(i => i.Chiave == chiave, ct);
        if (imp is null)
        {
            imp = new Impostazione { Chiave = chiave };
            db.Impostazioni.Add(imp);
        }
        imp.Valore = valore;
        imp.ModificatoIl = orologio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DatiAzienda
{
    public string? RagioneSociale { get; set; }
    public string? Indirizzo { get; set; }
    public string? Cap { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Pec { get; set; }

    /// <summary>Condizioni generali stampate in fondo a ogni ordine di acquisto.</summary>
    public string? CondizioniOrdine { get; set; }

    public bool Compilati => !string.IsNullOrWhiteSpace(RagioneSociale);

    /// <summary>Righe dell'intestazione: ragione sociale, indirizzo, recapiti, partita IVA.</summary>
    public IEnumerable<string> Intestazione()
    {
        if (!string.IsNullOrWhiteSpace(RagioneSociale)) yield return RagioneSociale!;
        var indirizzo = string.Join(" ", new[] { Indirizzo, "–", Cap, Comune, string.IsNullOrWhiteSpace(Provincia) ? null : $"({Provincia})" }
            .Where(x => !string.IsNullOrWhiteSpace(x))).Trim(' ', '–');
        if (indirizzo.Length > 0) yield return indirizzo;
        var recapiti = string.Join(" · ", new[] { Telefono is { Length: > 0 } ? "Tel. " + Telefono : null, Email, Pec is { Length: > 0 } ? "PEC " + Pec : null }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (recapiti.Length > 0) yield return recapiti;
        var fiscali = string.Join(" · ", new[] { PartitaIva is { Length: > 0 } ? "P. IVA " + PartitaIva : null, CodiceFiscale is { Length: > 0 } ? "C.F. " + CodiceFiscale : null }
            .Where(x => x is not null));
        if (fiscali.Length > 0) yield return fiscali;
    }
}
