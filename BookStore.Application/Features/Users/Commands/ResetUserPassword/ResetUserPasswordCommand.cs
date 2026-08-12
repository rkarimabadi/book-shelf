using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Users.Commands.ResetUserPassword;

public record ResetUserPasswordCommand(
    Guid Id,
    string NewPassword,
    Guid CurrentUserId) : IRequest<ErrorOr<Success>>;

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.");
    }
}

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public ResetUserPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        if (command.Id == command.CurrentUserId)
        {
            return UserErrors.Validation.CannotModifySelf;
        }

        var user = await _userRepository.GetByIdAsync(command.Id, cancellationToken);
        if (user is null)
        {
            return UserErrors.Validation.UserNotFound(command.Id);
        }

        // PBKDF2 hashes are salted; always hash the new password fresh (never compare stored hashes).
        var newPasswordHash = _passwordHasher.HashPassword(command.NewPassword);

        // ChangePassword validates activity, sets the hash, revokes every refresh token
        // (forced re-login) and raises PasswordChangedEvent.
        var result = user.ChangePassword(newPasswordHash);
        if (result.IsError)
        {
            return result.Errors;
        }

        _userRepository.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
