using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.ResendEmailVerification;

/// <summary>Mails a fresh confirmation code. Always succeeds from the caller's point of view.</summary>
public record ResendEmailVerificationCommand(string Email) : IRequest<ErrorOr<int>>;

public class ResendEmailVerificationCommandValidator : AbstractValidator<ResendEmailVerificationCommand>
{
    public ResendEmailVerificationCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");
    }
}

/// <summary>Returns the seconds until another code may be requested.</summary>
public class ResendEmailVerificationCommandHandler : IRequestHandler<ResendEmailVerificationCommand, ErrorOr<int>>
{
    private readonly IUserRepository _userRepository;
    private readonly EmailVerificationMailer _mailer;

    public ResendEmailVerificationCommandHandler(IUserRepository userRepository, EmailVerificationMailer mailer)
    {
        _userRepository = userRepository;
        _mailer = mailer;
    }

    public async Task<ErrorOr<int>> Handle(ResendEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = _userRepository.GetByEmail(command.Email);

        // Never reveal whether an account exists or is already confirmed: pretend a code went out.
        if (user is null || user.EmailConfirmed || !user.IsActive)
        {
            return (int)EmailVerificationCodes.ResendInterval.TotalSeconds;
        }

        return await _mailer.SendAsync(user, cancellationToken);
    }
}
