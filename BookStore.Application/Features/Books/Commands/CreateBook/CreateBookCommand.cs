using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;

namespace BookStore.Application.Features.Books.Commands.CreateBook;

public record CreateBookCommand(
    string Title,
    string Author,
    string Category,
    string Description,
    string CoverImagePath,
    string FilePath) : IRequest<ErrorOr<Book>>;

public class CreateBookCommandValidator : AbstractValidator<CreateBookCommand>
{
    public CreateBookCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.Author)
            .NotEmpty().WithMessage("Author is required.")
            .MaximumLength(100).WithMessage("Author must not exceed 100 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(BookCategories.IsValid).WithMessage("Category is invalid.");

        RuleFor(x => x.FilePath)
            .NotEmpty().WithMessage("Book file is required.");
    }
}

public class CreateBookCommandHandler : IRequestHandler<CreateBookCommand, ErrorOr<Book>>
{
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBookCommandHandler(IBookRepository bookRepository, IUnitOfWork unitOfWork)
    {
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<Book>> Handle(CreateBookCommand command, CancellationToken cancellationToken)
    {
        var bookResult = Book.Create(
            command.Title,
            command.Author,
            command.Category,
            command.Description,
            command.CoverImagePath,
            command.FilePath);

        if (bookResult.IsError)
        {
            return bookResult.Errors;
        }

        var book = bookResult.Value;
        _bookRepository.Add(book);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return book;
    }
}
