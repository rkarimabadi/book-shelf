using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Users;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Library.Commands.AddBookToLibrary;

public record AddBookToLibraryCommand(Guid UserId, Guid BookId) : IRequest<ErrorOr<Success>>;

public class AddBookToLibraryCommandValidator : AbstractValidator<AddBookToLibraryCommand>
{
    public AddBookToLibraryCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.BookId)
            .NotEmpty().WithMessage("Book id is required.");
    }
}

public class AddBookToLibraryCommandHandler : IRequestHandler<AddBookToLibraryCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AddBookToLibraryCommandHandler(
        IUserRepository userRepository,
        IBookRepository bookRepository,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Success>> Handle(AddBookToLibraryCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("User.NotFound", "User not found.");
        }

        var book = await _bookRepository.GetByIdAsync(command.BookId, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(command.BookId);
        }

        var result = user.AddToLibrary(command.BookId);
        if (result.IsError)
        {
            return result.Errors;
        }

        _userRepository.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
