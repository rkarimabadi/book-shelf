using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Authentication.Commands.ForgotPassword;

public record ForgotPasswordCommand(
    string Email,
    string ResetLinkBase) : IRequest<ErrorOr<Success>>;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.ResetLinkBase)
            .NotEmpty().WithMessage("Reset link base is required.");
    }
}

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ErrorOr<Success>>
{
    private const string UserNotFoundCode = "User.NotFound";

    private readonly IAuthenticationService _authenticationService;
    private readonly IEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IAuthenticationService authenticationService,
        IEmailSender emailSender,
        IUnitOfWork unitOfWork,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _authenticationService = authenticationService;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var result = _authenticationService.RequestPasswordReset(command.Email);
        if (result.IsError)
        {
            // Never reveal whether an account exists — an unknown email still returns success.
            if (result.FirstError.Code == UserNotFoundCode)
            {
                return Result.Success;
            }

            return result.Errors;
        }

        var (user, resetToken) = result.Value;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var resetLink = $"{command.ResetLinkBase}?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(resetToken)}";

        var htmlBody = $"""
            <div dir="rtl" style="font-family: Tahoma, Arial, sans-serif; max-width: 480px; margin: 0 auto; color: #333;">
              <h2 style="margin-bottom: 16px;">بازیابی رمز عبور</h2>
              <p>برای تعیین رمز عبور جدید روی دکمهٔ زیر کلیک کنید:</p>
              <p style="margin: 24px 0;">
                <a href="{resetLink}" style="display: inline-block; padding: 12px 28px; background: #007AFF; color: #fff; text-decoration: none; border-radius: 8px;">بازیابی رمز عبور</a>
              </p>
              <p style="color: #888; font-size: 12px; line-height: 1.8;">
                این لینک تا ۱ ساعت اعتبار دارد و فقط یک بار قابل استفاده است.<br/>
                اگر شما این درخواست را نداده‌اید، می‌توانید این ایمیل را نادیده بگیرید.
              </p>
            </div>
            """;

        try
        {
            await _emailSender.SendAsync(user.Email, "بازیابی رمز عبور", htmlBody, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log loudly but still return success: reporting a send failure would let an
            // attacker distinguish existing accounts (error) from unknown ones (success),
            // which defeats the no-enumeration guarantee of this endpoint.
            _logger.LogError(ex, "Failed to send password-reset email to {Email}", user.Email);
        }

        return Result.Success;
    }
}
