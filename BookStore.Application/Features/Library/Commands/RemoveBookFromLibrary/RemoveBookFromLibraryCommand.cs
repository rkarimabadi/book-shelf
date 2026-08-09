using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Library.Commands.RemoveBookFromLibrary;

public record RemoveBookFromLibraryCommand(Guid UserId, Guid BookId) : IRequest<ErrorOr<Success>>;

public class RemoveBookFromLibraryCommandValidator : AbstractValidator<RemoveBookFromLibraryCommand>
{
    public RemoveBookFromLibraryCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.BookId)
            .NotEmpty().WithMessage("Book id is required.");
    }
}

public class RemoveBookFromLibraryCommandHandler : IRequestHandler<RemoveBookFromLibraryCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveBookFromLibraryCommandHandler(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(RemoveBookFromLibraryCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("User.NotFound", "User not found.");
        }

        var result = user.RemoveFromLibrary(command.BookId);
        if (result.IsError)
        {
            return result.Errors;
        }

        _userRepository.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
