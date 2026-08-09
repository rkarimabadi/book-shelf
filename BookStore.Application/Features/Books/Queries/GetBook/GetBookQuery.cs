using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Queries.GetBook;

public record GetBookQuery(Guid Id) : IRequest<ErrorOr<Book>>;

public class GetBookQueryHandler : IRequestHandler<GetBookQuery, ErrorOr<Book>>
{
    private readonly IBookRepository _bookRepository;

    public GetBookQueryHandler(IBookRepository bookRepository)
    {
        _bookRepository = bookRepository;
    }

    public async Task<ErrorOr<Book>> Handle(GetBookQuery query, CancellationToken cancellationToken)
    {
        var book = await _bookRepository.GetByIdAsync(query.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(query.Id);
        }

        return book;
    }
}
