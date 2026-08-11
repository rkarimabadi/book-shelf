using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Authentication.Commands.ChangePassword;

public record ChangePasswordCommand(
    string Email,
    string CurrentPassword,
    string NewPassword) : IRequest<ErrorOr<Success>>;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.");
    }
}

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, ErrorOr<Success>>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public ChangePasswordCommandHandler(
        IAuthenticationService authenticationService,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _authenticationService = authenticationService;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var userResult = _authenticationService.GetUserByEmail(command.Email);
        if (userResult.IsError)
        {
            return userResult.Errors;
        }

        // PBKDF2 hashes are salted, so the current password must be verified against the
        // stored hash rather than comparing freshly hashed strings (see pitfall 2).
        if (!_passwordHasher.VerifyPassword(command.CurrentPassword, userResult.Value.PasswordHash))
        {
            return UserErrors.Validation.InvalidCurrentPassword;
        }

        var newPasswordHash = _passwordHasher.HashPassword(command.NewPassword);

        var result = _authenticationService.ChangePassword(command.Email, newPasswordHash);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
