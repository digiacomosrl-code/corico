using System.Net;
using Corico.Core;
using Corico.Core.Backup;
using Corico.Core.Configurazione;
using Corico.Core.Moduli;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo;
using Corico.Web.Components;
using Corico.Web.Infrastruttura;
using Microsoft.AspNetCore.Components.Server.Circuits;

var builder = WebApplication.CreateBuilder(args);

// Configurazione e cartella dati (separata dalla cartella del programma, sez. 3.3).
builder.Services.Configure<CoricoOpzioni>(builder.Configuration.GetSection(CoricoOpzioni.Sezione));
var opzioni = builder.Configuration.GetSection(CoricoOpzioni.Sezione).Get<CoricoOpzioni>() ?? new CoricoOpzioni();
var percorsi = new PercorsiDati(string.IsNullOrWhiteSpace(opzioni.CartellaDati)
    ? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "dati-sviluppo")
    : opzioni.CartellaDati);
percorsi.CreaCartelle();

builder.Logging.AddProvider(new FileLoggerProvider(percorsi.Log));

// Raggiungibile solo dal PC stesso (sez. 3.1 e 4).
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, opzioni.Porta));

// Moduli funzionali: ognuno è attivo salvo disattivazione esplicita in configurazione.
ICoricoModulo[] moduliDisponibili = [new PreventivoModulo()];
var moduliAttivi = moduliDisponibili.Where(m => !opzioni.Moduli.TryGetValue(m.Codice, out var attivo) || attivo).ToList();

builder.Services.AggiungiCorico(percorsi, moduliAttivi);
builder.Services.AddScoped<CircuitHandler, RegistraAttivitaCircuitHandler>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.GetRequiredService<IMigrazioniDatabase>().ApplicaAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
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

app.Logger.LogInformation("Corico avviato su http://127.0.0.1:{Porta} — cartella dati {Cartella}", opzioni.Porta, percorsi.Radice);
app.Run();
