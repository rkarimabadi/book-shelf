using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.ExternalLogin;

public record ExternalLoginCommand(
    string Email,
    string FirstName,
    string LastName) : IRequest<ErrorOr<AuthenticationResult>>;

public class ExternalLoginCommandValidator : AbstractValidator<ExternalLoginCommand>
{
    public ExternalLoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");
    }
}

public class ExternalLoginCommandHandler : IRequestHandler<ExternalLoginCommand, ErrorOr<AuthenticationResult>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;

    public ExternalLoginCommandHandler(
        IAuthenticationService authenticationService,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IUnitOfWork unitOfWork)
    {
        _authenticationService = authenticationService;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<AuthenticationResult>> Handle(ExternalLoginCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var email = command.Email.Trim().ToLowerInvariant();

        var existingUserResult = _authenticationService.GetUserByEmail(email);
        if (existingUserResult.IsError)
        {
            // First-time external sign-in: auto-provision the account. The password is a
            // PBKDF2 hash of a random, never-known secret, so the password form can never
            // be used against it (VerifyPassword simply fails) while "forgot password"
            // still works to give the user a real password later if they want one.
            // hasPassword:false marks the account as passwordless so the change-password
            // flow lets the user SET a password without proving a current one.
            var randomPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var hashedPassword = _passwordHasher.HashPassword(randomPassword);

            var registerResult = _authenticationService.RegisterUser(
                email,
                hashedPassword,
                NormalizeName(command.FirstName),
                NormalizeName(command.LastName),
                hasPassword: false);

            if (registerResult.IsError)
            {
                return registerResult.Errors;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // LoginExternalUser re-checks IsActive for existing accounts, so a deactivated
        // user gets UserInactive here instead of a session.
        var loginResult = _authenticationService.LoginExternalUser(email);
        if (loginResult.IsError)
        {
            return loginResult.Errors;
        }

        var (user, refreshToken) = loginResult.Value;

        var token = _jwtTokenGenerator.GenerateToken(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role.ToString());

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role.ToString(),
            token,
            refreshToken);
    }

    // Google may omit family_name for some accounts; User.Create requires non-empty names.
    private static string NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "-" : name.Trim();
}
