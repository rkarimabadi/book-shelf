using System.Net;
using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Users;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Authentication.Common;

/// <summary>
/// Issues a registration code on the (already tracked) user, stores it, then mails it. When the
/// mail cannot be sent the code is withdrawn again, so the owner can ask for another one straight
/// away instead of waiting for a code that never arrived.
/// </summary>
public sealed class EmailVerificationMailer
{
    private const string Product = "نون گرد";

    private readonly IEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<EmailVerificationMailer> _logger;

    public EmailVerificationMailer(
        IEmailSender emailSender,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ILogger<EmailVerificationMailer> logger)
    {
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Returns how many seconds the owner must wait before asking for another code (0 when the mail
    /// could not be sent, so the page offers «ارسال دوباره» immediately). A request made inside the
    /// cooldown sends nothing and just reports the remaining wait.
    /// </summary>
    public async Task<ErrorOr<int>> SendAsync(User user, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var code = user.IssueEmailVerificationCode(now);
        if (code.IsError)
        {
            return code.FirstError == UserErrors.Validation.VerificationCodeResendTooSoon
                ? user.SecondsUntilNextVerificationCode(now)
                : code.Errors;
        }

        // Stored before the mail leaves, so a code in someone's inbox always exists here too.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailSender.SendAsync(user.Email, $"کد تأیید {Product}: {code.Value}", BuildHtml(user, code.Value), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send the email-verification code to {Email}", user.Email);
            user.WithdrawEmailVerificationCode();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return 0;
        }

        return user.SecondsUntilNextVerificationCode(now);
    }

    // The code is shown in Latin digits so it copies cleanly into any keyboard layout.
    private static string BuildHtml(User user, string code)
    {
        var name = WebUtility.HtmlEncode(user.FirstName);
        var minutes = (int)EmailVerificationCodes.Lifetime.TotalMinutes;

        return $"""
            <div dir="rtl" style="font-family: Tahoma, Arial, sans-serif; max-width: 480px; margin: 0 auto; color: #333; line-height: 1.9;">
              <h2 style="margin-bottom: 16px;">تأیید ایمیل</h2>
              <p>سلام {name}،</p>
              <p>برای تکمیل ثبت‌نام در «{Product}» این کد را در صفحهٔ تأیید ایمیل وارد کنید:</p>
              <p dir="ltr" style="margin: 24px 0; text-align: center; font-family: Consolas, 'Courier New', monospace; font-size: 32px; font-weight: bold; letter-spacing: 8px;">{code}</p>
              <p style="color: #888; font-size: 12px;">
                این کد تا {minutes} دقیقه معتبر است.<br/>
                اگر شما در «{Product}» حساب نساخته‌اید، این ایمیل را نادیده بگیرید.
              </p>
            </div>
            """;
    }
}
