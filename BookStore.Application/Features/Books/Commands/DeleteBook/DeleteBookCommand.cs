using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Commands.DeleteBook;

public record DeleteBookCommand(Guid Id) : IRequest<ErrorOr<Success>>;

public class DeleteBookCommandHandler : IRequestHandler<DeleteBookCommand, ErrorOr<Success>>
{
    private readonly IBookRepository _bookRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteBookCommandHandler(IBookRepository bookRepository, IUnitOfWork unitOfWork)
    {
        _bookRepository = bookRepository;
        _unitOfWork = unitOfWork;
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

        return Result.Success;
    }
}
