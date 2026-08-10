using BookStore.Application.Common;
using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BookStore.Application.Features.Books.Commands.DeleteBook;

public record DeleteBookCommand(Guid Id) : IRequest<ErrorOr<Success>>;

public class DeleteBookCommandHandler : IRequestHandler<DeleteBookCommand, ErrorOr<Success>>
{
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DeleteBookCommandHandler> _logger;

    public DeleteBookCommandHandler(
        IBookRepository bookRepository,
        IUnitOfWork unitOfWork,
        IFileStorage fileStorage,
        ILogger<DeleteBookCommandHandler> logger)
    {
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(DeleteBookCommand command, CancellationToken cancellationToken)
    {
        var book = await _bookRepository.GetByIdAsync(command.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(command.Id);
        }

        book.Delete();

        _bookRepository.Delete(book);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await FileCleanup.DeleteIfExistsAsync(_fileStorage, _logger, book.CoverImagePath, "book deletion", cancellationToken);
        await FileCleanup.DeleteIfExistsAsync(_fileStorage, _logger, book.FilePath, "book deletion", cancellationToken);

        return Result.Success;
    }
}
