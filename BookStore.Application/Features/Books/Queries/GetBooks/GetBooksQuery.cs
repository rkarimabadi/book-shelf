using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Queries.GetBooks;

public record GetBooksQuery(
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 12,
    string? Category = null,
    string? Search = null) : IRequest<ErrorOr<GetBooksResult>>;

/// <summary>One page of books plus paging metadata (page/pageSize are the clamped effective values).</summary>
public record GetBooksResult(
    List<Book> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public class GetBooksQueryHandler : IRequestHandler<GetBooksQuery, ErrorOr<GetBooksResult>>
{
    private readonly IBookRepository _bookRepository;

    public GetBooksQueryHandler(IBookRepository bookRepository)
    {
        _bookRepository = bookRepository;
    }

    public async Task<ErrorOr<GetBooksResult>> Handle(GetBooksQuery query, CancellationToken cancellationToken)
    {
        // Defensive clamping: the handler is the single place that knows the real page bounds.
        // 500 is the ceiling so the admin catalog (which requests a single "all rows" page) is never
        // silently truncated; the public UI never asks for more than 12.
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 500);

        var (items, totalCount) = await _bookRepository.GetPageAsync(
            page, pageSize, query.IncludeInactive, query.Category, query.Search, cancellationToken);

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

        return new GetBooksResult(items, totalCount, page, pageSize, totalPages);
    }
}
