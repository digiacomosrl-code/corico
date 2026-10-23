using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Corico.Core.Ai;

/// <summary>Chiamata già autorizzata dal <see cref="ServizioAi"/> (AI attiva, tetto, permessi).</summary>
public sealed record ChiamataAi(string Chiave, string Modello, string Istruzioni, string Contenuto, string? SchemaJson, int MaxTokenRisposta);

/// <param name="Rifiutata">Il servizio ha declinato la richiesta per le proprie regole di sicurezza.</param>
public sealed record EsitoChiamataAi(string? Testo, string? Errore, int TokenIngresso, int TokenUscita, bool Rifiutata = false);

/// <summary>Collegamento al fornitore AI: sostituibile senza toccare i moduli (Modulo 3, §3).</summary>
public interface IClienteAi
{
    Task<EsitoChiamataAi> InviaAsync(ChiamataAi chiamata, CancellationToken ct);
}

/// <summary>
/// Fornitore Anthropic (modelli Claude). La risposta arriva in streaming, così le letture lunghe non scadono;
/// con uno schema JSON la risposta è vincolata allo schema (output strutturato).
/// Se il modello scelto declina la richiesta, il servizio la ripassa in automatico a un modello adatto (fallback lato server).
/// </summary>
public sealed class ClienteAnthropic : IClienteAi
{
    private readonly HttpMessageHandler? _gestore;

    public ClienteAnthropic() { }

    /// <summary>Per le prove: risposte simulate senza collegamento esterno.</summary>
    internal ClienteAnthropic(HttpMessageHandler gestore) => _gestore = gestore;

    public async Task<EsitoChiamataAi> InviaAsync(ChiamataAi chiamata, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = chiamata.Chiave, Timeout = TimeSpan.FromMinutes(30) };
        if (_gestore is not null) client = new AnthropicClient { ApiKey = chiamata.Chiave, HttpClient = new HttpClient(_gestore), MaxRetries = 0 };
        var parametri = new MessageCreateParams
        {
            Model = chiamata.Modello,
            MaxTokens = chiamata.MaxTokenRisposta,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            System = chiamata.Istruzioni,
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.High,
                Format = chiamata.SchemaJson is null ? null : new BetaJsonOutputFormat
                {
                    Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(chiamata.SchemaJson)!,
                },
            },
            Messages = [new BetaUserMessageParam { Content = chiamata.Contenuto }],
        };

        var testo = new StringBuilder();
        long ingresso = 0, uscita = 0;
        string? motivoFine = null;
        try
        {
            await foreach (var evento in client.Beta.Messages.CreateStreaming(parametri, ct))
            {
                if (evento.TryPickStart(out var inizio))
                {
                    ingresso = inizio.Message.Usage.InputTokens;
                }
                else if (evento.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var parte))
                {
                    testo.Append(parte.Text);
                }
                else if (evento.TryPickDelta(out var fine))
                {
                    uscita = fine.Usage.OutputTokens;
                    if (fine.Usage.InputTokens is { } i && i > 0) ingresso = i;
                    motivoFine = fine.Delta.StopReason?.Raw();
                }
            }
        }
        catch (AnthropicUnauthorizedException)
        {
            return new(null, "La chiave d'accesso al servizio AI non è valida: l'amministratore deve inserirla di nuovo.", 0, 0);
        }
        catch (AnthropicRateLimitException)
        {
            return new(null, "Il servizio AI è momentaneamente sovraccarico: riprova tra qualche minuto.", (int)ingresso, (int)uscita);
        }
        catch (AnthropicApiException ex)
        {
            return new(null, $"Il servizio AI ha risposto con un errore: {ex.Message}", (int)ingresso, (int)uscita);
        }

        return motivoFine switch
        {
            "refusal" => new(null, "Il servizio AI ha declinato la richiesta.", (int)ingresso, (int)uscita, Rifiutata: true),
            "max_tokens" => new(null, "La risposta dell'AI è stata interrotta perché troppo lunga: riprova con meno dati.", (int)ingresso, (int)uscita),
            _ => new(testo.ToString(), null, (int)ingresso, (int)uscita),
        };
    }
}
