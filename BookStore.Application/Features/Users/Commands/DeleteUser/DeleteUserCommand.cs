using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Users.Commands.DeleteUser;

public record DeleteUserCommand(
    Guid Id,
    Guid CurrentUserId) : IRequest<ErrorOr<Success>>;

public class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("User id is required.");
    }
}

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteUserCommandHandler(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
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

        // Cascade deletes remove the user's refresh tokens, password-reset tokens and
        // library entries; their books are untouched.
        _userRepository.Delete(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
