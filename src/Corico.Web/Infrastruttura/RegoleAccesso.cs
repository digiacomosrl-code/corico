using System.Net;
using System.Security.Claims;
using Corico.Core.Utenti;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Corico.Web.Infrastruttura;

public static class RegoleAccesso
{
    public static ClaimsPrincipal Principale(IdentitaUtente u) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new Claim(ClaimTypes.Name, u.Nome),
            new Claim(ClaimTypes.Role, u.Ruolo.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>
    /// Richiesta fatta sul PC sede (non inoltrata da Tailscale Serve): solo da qui si crea il primo amministratore.
    /// Tailscale Serve inoltra a 127.0.0.1 aggiungendo le intestazioni X-Forwarded-* e Tailscale-User-*.
    /// </summary>
    public static bool DalPcSede(HttpContext contesto)
    {
        var r = contesto.Request;
        var host = r.Host.Host;
        return contesto.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
            && host is "localhost" or "127.0.0.1"
            && !r.Headers.ContainsKey("X-Forwarded-For") && !r.Headers.ContainsKey("X-Forwarded-Host")
            && !r.Headers.Keys.Any(k => k.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Indirizzo di ritorno dopo l'accesso: solo percorsi interni a Corico.</summary>
    public static string Ritorno(string? ritorno) =>
        !string.IsNullOrEmpty(ritorno) && ritorno.StartsWith('/') && !ritorno.StartsWith("//") && !ritorno.StartsWith("/\\")
            && !ritorno.StartsWith("/accesso", StringComparison.OrdinalIgnoreCase)
            ? ritorno : "/";

    /// <summary>Comandi di emergenza: «--utenti elenco» e «--utenti reimposta-password &lt;nome utente&gt;».</summary>
    public static async Task<int> ComandoUtentiAsync(IServiceProvider servizi, string[] argomenti, TextWriter uscita)
    {
        var utenti = servizi.GetRequiredService<UtentiService>();
        try
        {
            switch (argomenti.FirstOrDefault())
            {
                case "elenco":
                    foreach (var u in await utenti.ElencoAsync())
                        await uscita.WriteLineAsync($"{u.NomeUtente,-20} {u.NomeCompleto,-30} {u.Ruolo,-15} {(u.Attivo ? "attivo" : "disattivato")}");
                    return 0;
                case "reimposta-password" when argomenti.Length > 1:
                    var provvisoria = await utenti.ReimpostaPasswordAsync(argomenti[1]);
                    await uscita.WriteLineAsync($"Password provvisoria per {UtentiService.Normalizza(argomenti[1])}: {provvisoria}");
                    await uscita.WriteLineAsync("Al prossimo accesso Corico chiederà di sceglierne una nuova.");
                    return 0;
                default:
                    await uscita.WriteLineAsync("Uso: Corico.Web.exe --utenti elenco | --utenti reimposta-password <nome utente>");
                    return 2;
            }
        }
        catch (Corico.Core.Dominio.OperazioneNonValidaException ex)
        {
            await uscita.WriteLineAsync("ERRORE: " + ex.Message);
            return 1;
        }
    }
}
