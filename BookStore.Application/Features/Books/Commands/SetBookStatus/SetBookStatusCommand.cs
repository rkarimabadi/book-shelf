using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Books.Commands.SetBookStatus;

public record SetBookStatusCommand(
    Guid Id,
    bool IsActive) : IRequest<ErrorOr<Book>>;

public class SetBookStatusCommandValidator : AbstractValidator<SetBookStatusCommand>
{
    public SetBookStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Book id is required.");
    }
}

public class SetBookStatusCommandHandler : IRequestHandler<SetBookStatusCommand, ErrorOr<Book>>
{
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetBookStatusCommandHandler(IBookRepository bookRepository, IUnitOfWork unitOfWork)
    {
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Book>> Handle(SetBookStatusCommand command, CancellationToken cancellationToken)
    {
        var book = await _bookRepository.GetByIdAsync(command.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(command.Id);
        }

        var result = command.IsActive ? book.Activate() : book.Deactivate();
        if (result.IsError)
        {
            return result.Errors;
        }

        _bookRepository.Update(book);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return book;
    }
}
