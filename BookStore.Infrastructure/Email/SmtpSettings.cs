namespace BookStore.Infrastructure.Email;

public class SmtpSettings
{
    public const string SectionName = "SmtpSettings";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@bookstore.local";
    public string FromName { get; set; } = "کتابفروشی";
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// When no host is configured the sender falls back to logging the message
    /// (dev convenience — the reset link is still visible in the server log).
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
