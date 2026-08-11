using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Users.Commands.SetUserRole;

public record SetUserRoleCommand(
    Guid Id,
    UserRole Role,
    Guid CurrentUserId) : IRequest<ErrorOr<Success>>;

public class SetUserRoleCommandValidator : AbstractValidator<SetUserRoleCommand>
{
    public SetUserRoleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Role is invalid.");
    }
}

public class SetUserRoleCommandHandler : IRequestHandler<SetUserRoleCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetUserRoleCommandHandler(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(SetUserRoleCommand command, CancellationToken cancellationToken)
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

        var result = user.ChangeRole(command.Role);
        if (result.IsError)
        {
            return result.Errors;
        }

        _userRepository.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
