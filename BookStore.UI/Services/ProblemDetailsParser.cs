using System.Text.Json;

namespace BookStore.UI.Services;

/// <summary>Extracts the most specific human-readable message from an RFC 7807 ProblemDetails JSON body.</summary>
public static class ProblemDetailsParser
{
    /// <summary>
    /// Returns the first available of: field-error message, title, detail.
    /// Returns null when the body is not parseable ProblemDetails.
    /// </summary>
    public static string? ReadMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // ValidationProblemDetails: { errors: { Field: [message] } }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                    {
                        var message = property.Value[0].GetString();
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            return message;
                        }
                    }
                }
            }

            if (root.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString()))
            {
                return title.GetString();
            }

            if (root.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
