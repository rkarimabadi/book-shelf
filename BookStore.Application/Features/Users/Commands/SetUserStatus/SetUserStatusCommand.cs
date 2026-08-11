using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Users.Commands.SetUserStatus;

public record SetUserStatusCommand(
    Guid Id,
    bool IsActive,
    Guid CurrentUserId) : IRequest<ErrorOr<Success>>;

public class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("User id is required.");
    }
}

public class SetUserStatusCommandHandler : IRequestHandler<SetUserStatusCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetUserStatusCommandHandler(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(SetUserStatusCommand command, CancellationToken cancellationToken)
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

        if (command.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        _userRepository.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
