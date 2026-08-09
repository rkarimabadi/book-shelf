using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Books.Commands.UpdateBook;

public record UpdateBookCommand(
    Guid Id,
    string Title,
    string Author,
    string Description,
    string? CoverImagePath,
    string? FilePath) : IRequest<ErrorOr<Book>>;

public class UpdateBookCommandValidator : AbstractValidator<UpdateBookCommand>
{
    public UpdateBookCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Book id is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.Author)
            .NotEmpty().WithMessage("Author is required.")
            .MaximumLength(100).WithMessage("Author must not exceed 100 characters.");
    }
}

public class UpdateBookCommandHandler : IRequestHandler<UpdateBookCommand, ErrorOr<Book>>
{
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBookCommandHandler(IBookRepository bookRepository, IUnitOfWork unitOfWork)
    {
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Book>> Handle(UpdateBookCommand command, CancellationToken cancellationToken)
    {
        var book = await _bookRepository.GetByIdAsync(command.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(command.Id);
        }

        var updateResult = book.UpdateDetails(
            command.Title,
            command.Author,
            command.Description,
            command.CoverImagePath,
            command.FilePath);

        if (updateResult.IsError)
        {
            return updateResult.Errors;
        }

        _bookRepository.Update(book);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return book;
    }
}
