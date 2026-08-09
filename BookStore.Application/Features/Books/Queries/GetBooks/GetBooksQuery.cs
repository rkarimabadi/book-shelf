using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Queries.GetBooks;

public record GetBooksQuery : IRequest<ErrorOr<List<Book>>>;

public class GetBooksQueryHandler : IRequestHandler<GetBooksQuery, ErrorOr<List<Book>>>
{
    private readonly IBookRepository _bookRepository;

    public GetBooksQueryHandler(IBookRepository bookRepository)
    {
        _bookRepository = bookRepository;
    }

    public async Task<ErrorOr<List<Book>>> Handle(GetBooksQuery query, CancellationToken cancellationToken)
    {
        return await _bookRepository.GetAllAsync(cancellationToken);
    }
}
