using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.Login;

public record LoginCommand(
    string Email,
    string Password) : IRequest<ErrorOr<AuthenticationResult>>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, ErrorOr<AuthenticationResult>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;

    public LoginCommandHandler(
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

    public async Task<ErrorOr<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var userResult = _authenticationService.GetUserByEmail(command.Email);
        if (userResult.IsError)
        {
            return UserErrors.Validation.InvalidCredentials;
        }

        if (!_passwordHasher.VerifyPassword(command.Password, userResult.Value.PasswordHash))
        {
            return UserErrors.Validation.InvalidCredentials;
        }

        var loginResult = _authenticationService.LoginUser(command.Email, userResult.Value.PasswordHash);
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
}
