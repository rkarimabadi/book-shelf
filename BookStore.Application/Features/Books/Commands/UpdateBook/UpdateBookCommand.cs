using BookStore.Application.Common;
using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Books.Commands.UpdateBook;

public record UpdateBookCommand(
    Guid Id,
    string Title,
    string Author,
    string Category,
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

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(BookCategories.IsValid).WithMessage("Category is invalid.");
    }
}

public class UpdateBookCommandHandler : IRequestHandler<UpdateBookCommand, ErrorOr<Book>>
{
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<UpdateBookCommandHandler> _logger;

    public UpdateBookCommandHandler(
        IBookRepository bookRepository,
        IUnitOfWork unitOfWork,
        IFileStorage fileStorage,
        ILogger<UpdateBookCommandHandler> logger)
    {
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<ErrorOr<Book>> Handle(UpdateBookCommand command, CancellationToken cancellationToken)
    {
        var book = await _bookRepository.GetByIdAsync(command.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(command.Id);
        }

        var oldCoverImagePath = book.CoverImagePath;
        var oldFilePath = book.FilePath;

        var updateResult = book.UpdateDetails(
            command.Title,
            command.Author,
            command.Category,
            command.Description,
            command.CoverImagePath,
            command.FilePath);

        if (updateResult.IsError)
        {
            return updateResult.Errors;
        }

        _bookRepository.Update(book);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.CoverImagePath is not null && !string.IsNullOrWhiteSpace(oldCoverImagePath) && oldCoverImagePath != command.CoverImagePath)
        {
            await FileCleanup.DeleteIfExistsAsync(_fileStorage, _logger, oldCoverImagePath, "book cover replacement", cancellationToken);
        }

        if (command.FilePath is not null && !string.IsNullOrWhiteSpace(oldFilePath) && oldFilePath != command.FilePath)
        {
            await FileCleanup.DeleteIfExistsAsync(_fileStorage, _logger, oldFilePath, "book file replacement", cancellationToken);
        }

        return book;
    }
}
