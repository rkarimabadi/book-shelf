using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Queries.GetBooks;

public record GetBooksQuery(bool IncludeInactive = false) : IRequest<ErrorOr<List<Book>>>;

public class GetBooksQueryHandler : IRequestHandler<GetBooksQuery, ErrorOr<List<Book>>>
{
    private readonly IBookRepository _bookRepository;

    public GetBooksQueryHandler(IBookRepository bookRepository)
    {
        _bookRepository = bookRepository;
    }

    public async Task<ErrorOr<List<Book>>> Handle(GetBooksQuery query, CancellationToken cancellationToken)
    {
        // IncludeInactive is only ever set by the admin book list; the public catalog always
        // returns active books only.
        return query.IncludeInactive
            ? await _bookRepository.GetAllAsync(cancellationToken)
            : await _bookRepository.GetActiveAsync(cancellationToken);
    }
}
