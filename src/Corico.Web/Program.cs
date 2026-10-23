using System.Net;
using Corico.Core;
using Corico.Core.Aggiornamento;
using Corico.Core.Backup;
using Corico.Core.Configurazione;
using Corico.Core.Moduli;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo;
using Corico.Web.Components;
using Corico.Web.Infrastruttura;
using System.Security.Claims;
using Corico.Core.Utenti;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

// "--aggiornamento <comando>": passi dell'aggiornamento guidato, senza avviare il server web (scripts\aggiorna.ps1).
// "--utenti <comando>": gestione di emergenza degli utenti dal PC sede (es. password dell'amministratore dimenticata).
var comandoAggiornamento = args.Length > 0 && args[0] == ControlliAggiornamento.Argomento ? args[1..] : null;
var comandoUtenti = args.Length > 0 && args[0] == "--utenti" ? args[1..] : null;

var builder = WebApplication.CreateBuilder(comandoAggiornamento is null && comandoUtenti is null ? args : []);

// Configurazione e cartella dati (separata dalla cartella del programma, sez. 3.3).
builder.Services.Configure<CoricoOpzioni>(builder.Configuration.GetSection(CoricoOpzioni.Sezione));
var opzioni = builder.Configuration.GetSection(CoricoOpzioni.Sezione).Get<CoricoOpzioni>() ?? new CoricoOpzioni();
var percorsi = new PercorsiDati(string.IsNullOrWhiteSpace(opzioni.CartellaDati)
    ? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "dati-sviluppo")
    : opzioni.CartellaDati);
percorsi.CreaCartelle();

builder.Logging.AddProvider(new FileLoggerProvider(percorsi.Log));

// In ascolto solo sul PC stesso (sez. 3.1 e 4): gli altri uffici arrivano tramite Tailscale Serve,
// che inoltra a 127.0.0.1 le richieste dei soli dispositivi autorizzati, con un indirizzo HTTPS *.ts.net.
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, opzioni.Porta));
builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;*.ts.net";

// Accesso con utente e password per tutti, anche dal PC sede (sez. 4).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/accesso";
        o.LogoutPath = "/esci";
        o.AccessDeniedPath = "/";
        o.ReturnUrlParameter = "ritorno";
        o.Cookie.Name = "Corico.Accesso";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(opzioni.MinutiInattivita);
        o.SlidingExpiration = true;
        // A ogni richiesta: utente ancora attivo e ruolo aggiornato (le modifiche dell'amministratore valgono subito).
        o.Events.OnValidatePrincipal = async contesto =>
        {
            var utenti = contesto.HttpContext.RequestServices.GetRequiredService<UtentiService>();
            var identita = SessioneUtente.IdDa(contesto.Principal!) is { } id ? await utenti.IdentitaAsync(id) : null;
            if (identita is not { Attivo: true })
            {
                contesto.RejectPrincipal();
                await contesto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            else if (contesto.Principal!.FindFirstValue(ClaimTypes.Role) != identita.Ruolo.ToString())
            {
                contesto.ReplacePrincipal(RegoleAccesso.Principale(identita));
                contesto.ShouldRenew = true;
            }
        };
    });
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();

// Moduli funzionali: ognuno è attivo salvo disattivazione esplicita in configurazione.
ICoricoModulo[] moduliDisponibili = [new PreventivoModulo()];
var moduliAttivi = moduliDisponibili.Where(m => !opzioni.Moduli.TryGetValue(m.Codice, out var attivo) || attivo).ToList();

builder.Services.AggiungiCorico(percorsi, moduliAttivi);
builder.Services.AddScoped<CircuitHandler, RegistraAttivitaCircuitHandler>();
builder.Services.AddScoped<CircuitHandler, UtenteCircuitHandler>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (comandoAggiornamento is not null)
{
    Environment.ExitCode = await ControlliAggiornamento.EseguiAsync(app.Services, comandoAggiornamento, Console.Out);
    return;
}

if (comandoUtenti is not null)
{
    await app.Services.GetRequiredService<IMigrazioniDatabase>().ApplicaAsync();
    Environment.ExitCode = await RegoleAccesso.ComandoUtentiAsync(app.Services, comandoUtenti, Console.Out);
    return;
}

await app.Services.GetRequiredService<IMigrazioniDatabase>().ApplicaAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
// Utente corrente per le richieste HTTP (pagine, stampe, esportazioni) e per la connessione del circuito che ne deriva.
app.Use(async (contesto, prossimo) =>
{
    var identita = SessioneUtente.IdDa(contesto.User) is { } id
        ? await contesto.RequestServices.GetRequiredService<UtentiService>().IdentitaAsync(id)
        : null;
    contesto.RequestServices.GetRequiredService<SessioneUtente>().Identita = identita;
    using (UtenteCorrente.Imposta(identita))
        await prossimo(contesto);
});
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapGet("/esci", async (HttpContext contesto, UtentiService utenti, string? motivo) =>
{
    if (SessioneUtente.IdDa(contesto.User) is { } id)
        await utenti.RegistraUscitaAsync(id, motivo == "inattivita" ? "sessione scaduta per inattività" : "uscita");
    await contesto.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect(motivo == "inattivita" ? "/accesso?scaduta=1" : "/accesso");
}).AllowAnonymous();
// Rinnovo del cookie di accesso mentre si lavora nel circuito (la connessione Blazor non lo rinnova da sola).
app.MapGet("/rinnova", () => Results.NoContent());
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(moduliAttivi.Select(m => m.AssemblyInterfaccia).Distinct().ToArray());

app.MapGet("/documenti/{id:guid}/scarica", async (Guid id, DocumentiService documenti, CancellationToken ct) =>
{
    var file = await documenti.ApriAsync(id, ct);
    return file is null
        ? Results.NotFound()
        : Results.File(file.Value.Contenuto, file.Value.TipoContenuto, file.Value.Nome);
});

app.MapGet("/documenti/indice.csv", async (DocumentiService documenti, CancellationToken ct) =>
{
    var csv = await documenti.IndiceCsvAsync(ct);
    var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
    return Results.File(bytes, "text/csv", $"indice-documenti-{DateTime.Now:yyyyMMdd}.csv");
});

foreach (var modulo in moduliAttivi) modulo.MappaIndirizzi(app);

app.Logger.LogInformation("Corico avviato su http://127.0.0.1:{Porta} — cartella dati {Cartella}", opzioni.Porta, percorsi.Radice);
app.Run();
