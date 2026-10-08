using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.ConfirmEmail;

/// <summary>Confirms the address with the mailed code and, on success, signs the user in.</summary>
public record ConfirmEmailCommand(
    string Email,
    string Code) : IRequest<ErrorOr<AuthenticationResult>>;

public class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.")
            .MaximumLength(32).WithMessage("Verification code is incorrect.");
    }
}

public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, ErrorOr<AuthenticationResult>>
{
    private readonly IUserRepository _userRepository;
    private readonly IAuthenticationService _authenticationService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ConfirmEmailCommandHandler(
        IUserRepository userRepository,
        IAuthenticationService authenticationService,
        IJwtTokenGenerator jwtTokenGenerator,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _userRepository = userRepository;
        _authenticationService = authenticationService;
        _jwtTokenGenerator = jwtTokenGenerator;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ErrorOr<AuthenticationResult>> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = _userRepository.GetByEmail(command.Email);

        // An unknown address, and an already-confirmed one (that must sign in with its password),
        // look exactly like a code that no longer works — nothing leaks about who is registered.
        if (user is null || user.EmailConfirmed)
        {
            return UserErrors.Validation.VerificationCodeExpired;
        }

        var confirmed = user.ConfirmEmail(command.Code, _clock.UtcNow);

        // Saved either way: a wrong guess is counted against the code.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (confirmed.IsError)
        {
            return confirmed.Errors;
        }

        var loginResult = _authenticationService.LoginConfirmedUser(user);
        if (loginResult.IsError)
        {
            return loginResult.Errors;
        }

        var (_, refreshToken) = loginResult.Value;

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
}
