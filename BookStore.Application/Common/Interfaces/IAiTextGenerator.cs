using BookStore.Core.Domain.Translation;
using ErrorOr;

namespace BookStore.Application.Common.Interfaces;

/// <summary>Everything needed to call the configured AI provider (the key in plain text).</summary>
public sealed record AiConnection(
    AiProviderType Provider,
    string Endpoint,
    string? ApiKey,
    string Model);

/// <summary>One-shot text generation against the admin-configured AI provider.</summary>
public interface IAiTextGenerator
{
    /// <summary>
    /// Sends one system + user prompt and returns the model's text. Errors carry the codes
    /// <c>Ai.Unauthorized</c>, <c>Ai.NotFound</c>, <c>Ai.BadRequest</c> (fatal for a job),
    /// <c>Ai.Truncated</c> and <c>Ai.Failed</c> (worth retrying).
    /// </summary>
    Task<ErrorOr<string>> GenerateAsync(
        AiConnection connection,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
