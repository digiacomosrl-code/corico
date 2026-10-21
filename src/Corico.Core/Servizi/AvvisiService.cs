using Corico.Core.Backup;
using Corico.Core.Configurazione;
using Corico.Core.Dominio;
using Microsoft.Extensions.Options;

namespace Corico.Core.Servizi;

public enum GravitaAvviso { Informazione, Attenzione, Errore }

public sealed record Avviso(GravitaAvviso Gravita, string Problema, string AzioneSuggerita, string? Indirizzo = null);

/// <summary>Avvisi di sistema mostrati in evidenza (sez. 6.3, 6.5 e 19).</summary>
public sealed class AvvisiService(BackupService backup, IOptions<CoricoOpzioni> opzioni, TimeProvider orologio)
{
    public async Task<IReadOnlyList<Avviso>> AvvisiDiSistemaAsync(CancellationToken ct = default)
    {
        var avvisi = new List<Avviso>();
        var adesso = orologio.GetUtcNow().UtcDateTime;

        var ultimoLocale = await backup.UltimoAsync(TipoBackup.Locale, soloRiusciti: false, ct);
        if (ultimoLocale is { Riuscito: false })
            avvisi.Add(new(GravitaAvviso.Errore, "L'ultimo backup automatico non è riuscito: " + ultimoLocale.Messaggio,
                "Apri Amministrazione e prova a eseguire un backup manuale.", "/amministrazione"));

        var ultimoEsterno = await backup.UltimoBackupEsternoAsync(ct);
        var giorni = opzioni.Value.GiorniAvvisoBackupEsterno;
        if (ultimoEsterno is null)
            avvisi.Add(new(GravitaAvviso.Attenzione, "Non è mai stato eseguito un backup su disco esterno.",
                "Collega il disco esterno ed esegui il backup da Amministrazione.", "/amministrazione"));
        else if (adesso - ultimoEsterno.Value > TimeSpan.FromDays(giorni))
            avvisi.Add(new(GravitaAvviso.Attenzione,
                $"L'ultimo backup su disco esterno risale a {(int)(adesso - ultimoEsterno.Value).TotalDays} giorni fa.",
                "Collega il disco esterno ed esegui il backup da Amministrazione.", "/amministrazione"));

        return avvisi;
    }
}
