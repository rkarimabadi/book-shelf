using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Translation;

/// <summary>
/// Calls an OpenAI-compatible Chat Completions endpoint or the Anthropic Messages API.
/// Rate limits, server errors and timeouts are retried with backoff; authentication,
/// not-found and bad-request answers are returned at once (retrying cannot fix them).
/// </summary>
public sealed class AiTextGenerator : IAiTextGenerator
{
    private const int MaxAttempts = 4;
    private const int AnthropicMaxTokens = 16000;
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly ILogger<AiTextGenerator> _logger;

    public AiTextGenerator(HttpClient http, ILogger<AiTextGenerator> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> GenerateAsync(
        AiConnection connection,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        Error lastError = Error.Failure("Ai.Failed", "The AI provider did not answer.");

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            TimeSpan? retryAfter = null;
            try
            {
                using var request = BuildRequest(connection, systemPrompt, userPrompt);
                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return ReadText(connection.Provider, body);
                }

                lastError = MapStatus(response.StatusCode, body);
                if (lastError.Code != "Ai.Failed")
                {
                    return lastError; // 400/401/403/404: retrying cannot help
                }

                retryAfter = response.Headers.RetryAfter?.Delta;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = Error.Failure("Ai.Failed", "The AI provider timed out.");
            }
            catch (HttpRequestException ex)
            {
                lastError = Error.Failure("Ai.Failed", $"Could not reach the AI provider: {ex.Message}");
            }

            if (attempt < MaxAttempts)
            {
                var delay = retryAfter ?? TimeSpan.FromSeconds(5 * Math.Pow(3, attempt - 1));
                if (delay > TimeSpan.FromSeconds(90))
                {
                    delay = TimeSpan.FromSeconds(90);
                }

                _logger.LogWarning("AI request failed ({Error}); retrying in {Delay}s (attempt {Attempt}/{Max}).",
                    lastError.Description, (int)delay.TotalSeconds, attempt, MaxAttempts);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return lastError;
    }

    private static HttpRequestMessage BuildRequest(AiConnection connection, string systemPrompt, string userPrompt)
    {
        HttpRequestMessage request;
        if (connection.Provider == AiProviderType.Anthropic)
        {
            var body = new JsonObject
            {
                ["model"] = connection.Model,
                ["max_tokens"] = AnthropicMaxTokens,
                ["system"] = systemPrompt,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userPrompt })
            };

            request = new HttpRequestMessage(HttpMethod.Post, AnthropicUrl(connection.Endpoint))
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("anthropic-version", AnthropicVersion);
            if (!string.IsNullOrEmpty(connection.ApiKey))
            {
                request.Headers.Add("x-api-key", connection.ApiKey);
            }
        }
        else
        {
            // No max_tokens/temperature: several models (reasoning ones) reject them.
            var body = new JsonObject
            {
                ["model"] = connection.Model,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                    new JsonObject { ["role"] = "user", ["content"] = userPrompt })
            };

            request = new HttpRequestMessage(HttpMethod.Post, OpenAiUrl(connection.Endpoint))
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(connection.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
            }
        }

        return request;
    }

    /// <summary>https://api.openai.com/v1 → …/v1/chat/completions (a host without a path gets /v1).</summary>
    internal static string OpenAiUrl(string endpoint)
    {
        var url = endpoint.Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return new Uri(url).AbsolutePath is "/" or "" ? url + "/v1/chat/completions" : url + "/chat/completions";
    }

    /// <summary>https://api.anthropic.com (or …/v1) → …/v1/messages.</summary>
    internal static string AnthropicUrl(string endpoint)
    {
        var url = endpoint.Trim().TrimEnd('/');
        if (url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url + "/messages" : url + "/v1/messages";
    }

    private static ErrorOr<string> ReadText(AiProviderType provider, string body)
    {
        try
        {
            var root = JsonNode.Parse(body);
            if (provider == AiProviderType.Anthropic)
            {
                var text = string.Concat((root?["content"]?.AsArray() ?? [])
                    .Where(part => (string?)part?["type"] == "text")
                    .Select(part => (string?)part?["text"]));
                if ((string?)root?["stop_reason"] == "max_tokens")
                {
                    return Error.Failure("Ai.Truncated", "The AI answer was cut off (output token limit).");
                }

                return string.IsNullOrWhiteSpace(text)
                    ? Error.Failure("Ai.Failed", "The AI provider returned an empty answer.")
                    : text;
            }

            var choice = root?["choices"]?[0];
            if ((string?)choice?["finish_reason"] == "length")
            {
                return Error.Failure("Ai.Truncated", "The AI answer was cut off (output token limit).");
            }

            var content = choice?["message"]?["content"];
            var result = content switch
            {
                JsonValue value => (string?)value,
                JsonArray parts => string.Concat(parts.Select(p => (string?)p?["text"])),
                _ => null
            };

            return string.IsNullOrWhiteSpace(result)
                ? Error.Failure("Ai.Failed", "The AI provider returned an empty answer.")
                : result;
        }
        catch (JsonException)
        {
            return Error.Failure("Ai.Failed", "The AI provider returned an unreadable answer.");
        }
    }

    private static Error MapStatus(HttpStatusCode status, string body)
    {
        var detail = ProviderMessage(body);
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                Error.Unauthorized("Ai.Unauthorized", $"The AI provider rejected the API key ({(int)status}). {detail}".Trim()),
            HttpStatusCode.NotFound =>
                Error.NotFound("Ai.NotFound", $"The AI endpoint or model was not found (404). {detail}".Trim()),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.PaymentRequired =>
                Error.Validation("Ai.BadRequest", $"The AI provider refused the request ({(int)status}). {detail}".Trim()),
            _ => Error.Failure("Ai.Failed", $"The AI provider answered {(int)status}. {detail}".Trim())
        };
    }

    /// <summary>The provider's own error message (short), for the admin.</summary>
    private static string ProviderMessage(string body)
    {
        try
        {
            var root = JsonNode.Parse(body);
            var message = (string?)root?["error"]?["message"] ?? (string?)root?["message"];
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message.Length > 300 ? message[..300] : message;
            }
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        var text = body.Trim();
        return text.Length > 200 ? text[..200] : text;
    }
}
