using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.Register;

/// <summary>
/// Creates an UNCONFIRMED account and mails it a confirmation code. No session is issued here:
/// the owner signs in by entering the code (<c>ConfirmEmailCommand</c>).
/// </summary>
public record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName) : IRequest<ErrorOr<RegisterResult>>;

/// <summary>Where to send the code and how long until another one may be requested.</summary>
public record RegisterResult(string Email, int ResendAfterSeconds);

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");
    }
}

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ErrorOr<RegisterResult>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailVerificationMailer _mailer;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterCommandHandler(
        IAuthenticationService authenticationService,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        EmailVerificationMailer mailer,
        IUnitOfWork unitOfWork)
    {
        _authenticationService = authenticationService;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _mailer = mailer;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<RegisterResult>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var hashedPassword = _passwordHasher.HashPassword(command.Password);

        User user;
        var existing = _userRepository.GetByEmail(command.Email);
        if (existing is not null)
        {
            // A confirmed address is taken. An unconfirmed one was never proven by anybody, so a new
            // registration simply replaces it (otherwise a stranger could lock the real owner out).
            var restart = existing.RestartRegistration(hashedPassword, command.FirstName, command.LastName);
            if (restart.IsError)
            {
                return restart.Errors;
            }

            user = existing;
        }
        else
        {
            var registerResult = _authenticationService.RegisterUser(
                command.Email,
                hashedPassword,
                command.FirstName,
                command.LastName,
                emailConfirmed: false);

            if (registerResult.IsError)
            {
                return registerResult.Errors;
            }

            user = registerResult.Value;
        }

        // Saves the account and mails the code. If the mail fails the account still exists and the
        // confirmation page offers to send the code again straight away.
        var sent = await _mailer.SendAsync(user, cancellationToken);
        if (sent.IsError)
        {
            return sent.Errors;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterResult(user.Email, sent.Value);
    }
}
