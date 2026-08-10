using System.Net;
using System.Net.Mail;
using BookStore.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            // Dev fallback: no SMTP host configured — surface the email in the log so the
            // flow (e.g. the password-reset link) can still be verified locally.
            _logger.LogWarning(
                "SMTP is not configured (SmtpSettings:Host is empty); email not sent.\nTo: {To}\nSubject: {Subject}\n{Body}",
                to,
                subject,
                htmlBody);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.From, _settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(_settings.Username)
        };

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
