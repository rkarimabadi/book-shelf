namespace BookStore.Api.Common;

/// <summary>
/// Constants and helpers for the optional Google OAuth login. The feature is only
/// registered (and the UI button only shown) when GoogleOAuth:ClientId and
/// GoogleOAuth:ClientSecret are both configured — see Program.cs and AGENTS.md.
/// </summary>
public static class GoogleOAuthDefaults
{
    public const string SectionName = "GoogleOAuth";
    public const string SchemeName = "Google";
    public const string SignInScheme = "BookStore.GoogleExternal";
    public const string CallbackPath = "/api/auth/google-callback";

    public static bool IsConfigured(IConfiguration configuration)
    {
        var clientId = configuration[$"{SectionName}:ClientId"];
        var clientSecret = configuration[$"{SectionName}:ClientSecret"];

        return !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
    }

    /// <summary>
    /// Only same-origin relative paths are honored (mirrors the Login page's returnUrl
    /// guard and prevents open-redirects through the Google round trip).
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
}
