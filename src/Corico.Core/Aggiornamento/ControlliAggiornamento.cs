using System.Reflection;
using Corico.Core.Backup;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Moduli;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Corico.Core.Aggiornamento;

/// <summary>
/// Passi dell'aggiornamento guidato eseguiti dal programma (sez. 5.3), richiamati dallo script
/// <c>scripts\aggiorna.ps1</c> con <c>Corico.Web.exe --aggiornamento &lt;comando&gt;</c>, senza avviare il server web:
/// <list type="bullet">
/// <item><c>copia-sicurezza</c>: copia verificata del database prima di installare la nuova versione;</item>
/// <item><c>verifica</c>: applica le migrazioni e controlla database e commesse; codice di uscita 0 solo se tutto è superato.</item>
/// </list>
/// </summary>
public static class ControlliAggiornamento
{
    public const string Argomento = "--aggiornamento";

    /// <summary>Versione del programma con l'abbreviazione del commit, es. "0.9.0+e2a6455".</summary>
    public static string VersioneProgramma
    {
        get
        {
            var v = typeof(ControlliAggiornamento).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(ControlliAggiornamento).Assembly.GetName().Version?.ToString() ?? "?";
            var piu = v.IndexOf('+');
            return piu >= 0 && v.Length > piu + 8 ? v[..(piu + 8)] : v;
        }
    }

    /// <summary>Numero di versione senza il commit, es. "0.9.2", mostrato nella barra del programma.</summary>
    public static string VersioneBreve => VersioneProgramma.Split('+')[0];

    public static async Task<int> EseguiAsync(IServiceProvider servizi, string[] argomenti, TextWriter uscita, CancellationToken ct = default)
    {
        var percorsi = servizi.GetRequiredService<PercorsiDati>();
        switch (argomenti.FirstOrDefault())
        {
            case "copia-sicurezza":
                if (!File.Exists(percorsi.FileDatabase))
                {
                    await uscita.WriteLineAsync("COPIA=");
                    return 0;
                }
                try
                {
                    var file = BackupService.CopiaPrimaDiAggiornamento(percorsi, servizi.GetRequiredService<TimeProvider>().GetLocalNow().DateTime);
                    await uscita.WriteLineAsync("COPIA=" + file);
                    return 0;
                }
                catch (Exception ex)
                {
                    await uscita.WriteLineAsync("ERRORE=Copia di sicurezza non riuscita: " + ex.Message);
                    return 1;
                }

            case "verifica":
                var copia = Valore(argomenti, "--copia");
                var esiti = await VerificaAsync(servizi, copia, argomenti.Contains("--simula-errore"), ct);
                foreach (var e in esiti)
                    await uscita.WriteLineAsync($"{(e.Superato ? "OK    " : "ERRORE")} {e.Controllo}: {e.Dettaglio}");
                return esiti.All(e => e.Superato) ? 0 : 1;

            default:
                await uscita.WriteLineAsync("ERRORE=Comando sconosciuto. Comandi: copia-sicurezza, verifica [--copia <file>] [--simula-errore]");
                return 2;
        }
    }

    /// <summary>Migrazioni e controlli successivi. Si ferma al primo controllo bloccante non superato.</summary>
    public static async Task<IReadOnlyList<EsitoControllo>> VerificaAsync(IServiceProvider servizi, string? copiaSicurezza,
        bool simulaErrore, CancellationToken ct = default)
    {
        var esiti = new List<EsitoControllo>();
        var percorsi = servizi.GetRequiredService<PercorsiDati>();

        try
        {
            await servizi.GetRequiredService<IMigrazioniDatabase>().ApplicaAsync(ct);
            esiti.Add(new("Migrazioni del database", true, "applicate"));
        }
        catch (Exception ex)
        {
            esiti.Add(new("Migrazioni del database", false, ex.Message));
            return esiti;
        }

        esiti.Add(Controlla("Integrità del database", () =>
        {
            BackupService.VerificaIntegrita(percorsi.FileDatabase);
            using var conn = new SqliteConnection($"Data Source={percorsi.FileDatabase};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var r = cmd.ExecuteReader();
            if (r.Read()) throw new InvalidDataException("collegamenti non validi nella tabella " + r.GetString(0));
            return "integrity_check e foreign_key_check superati";
        }));

        try
        {
            await using var db = await servizi.GetRequiredService<IDbContextFactory<CoreDbContext>>().CreateDbContextAsync(ct);
            var commesse = await db.Commesse.CountAsync(ct);
            esiti.Add(new("Apertura dei dati", true, $"commesse leggibili: {commesse}"));
        }
        catch (Exception ex)
        {
            esiti.Add(new("Apertura dei dati", false, ex.Message));
        }

        foreach (var modulo in servizi.GetRequiredService<RegistroModuli>().Attivi)
        {
            try
            {
                esiti.AddRange(await modulo.ControlliDopoAggiornamentoAsync(servizi, ct));
            }
            catch (Exception ex)
            {
                esiti.Add(new($"Modulo {modulo.Nome}", false, ex.Message));
            }
        }

        if (simulaErrore)
            esiti.Add(new("Errore simulato", false, "richiesto per provare il ritorno alla versione precedente"));

        if (esiti.All(e => e.Superato))
        {
            if (copiaSicurezza is not null && File.Exists(copiaSicurezza))
                await servizi.GetRequiredService<BackupService>().RegistraCopiaAggiornamentoAsync(copiaSicurezza, ct);
            await using var db = await servizi.GetRequiredService<IDbContextFactory<CoreDbContext>>().CreateDbContextAsync(ct);
            db.RegistraOperazione("Aggiornamento", "Programma", null, null, $"Installata la versione {VersioneProgramma}");
            await db.SaveChangesAsync(ct);
        }
        return esiti;
    }

    private static EsitoControllo Controlla(string nome, Func<string> controllo)
    {
        try { return new(nome, true, controllo()); }
        catch (Exception ex) { return new(nome, false, ex.Message); }
    }

    private static string? Valore(string[] argomenti, string nome)
    {
        var i = Array.IndexOf(argomenti, nome);
        return i >= 0 && i + 1 < argomenti.Length ? argomenti[i + 1] : null;
    }
}
