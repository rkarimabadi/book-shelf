using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.Authentication.Common;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.Register;

public record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName) : IRequest<ErrorOr<AuthenticationResult>>;

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

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ErrorOr<AuthenticationResult>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterCommandHandler(
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

    public async Task<ErrorOr<AuthenticationResult>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var hashedPassword = _passwordHasher.HashPassword(command.Password);

        var registerResult = _authenticationService.RegisterUser(
            command.Email,
            hashedPassword,
            command.FirstName,
            command.LastName);

        if (registerResult.IsError)
        {
            return registerResult.Errors;
        }

        var user = registerResult.Value;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var loginResult = _authenticationService.LoginUser(command.Email, hashedPassword);
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