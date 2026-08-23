using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Books.Queries.GetBook;

public record GetBookQuery(Guid Id, bool IncludeInactive = false) : IRequest<ErrorOr<GetBookResult>>;

/// <summary>The book plus its translation-quality rating summary (average/count), shown on the details page.</summary>
public record GetBookResult(
    Book Book,
    double? RatingAverage,
    int RatingCount);

public class GetBookQueryHandler : IRequestHandler<GetBookQuery, ErrorOr<GetBookResult>>
{
    private readonly IBookRepository _bookRepository;
    private readonly ITranslationRatingRepository _ratingRepository;

    public GetBookQueryHandler(
        IBookRepository bookRepository,
        ITranslationRatingRepository ratingRepository)
    {
        _bookRepository = bookRepository;
        _ratingRepository = ratingRepository;
    }

    public async Task<ErrorOr<GetBookResult>> Handle(GetBookQuery query, CancellationToken cancellationToken)
    {
        var book = await _bookRepository.GetByIdAsync(query.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.Validation.NotFound(query.Id);
        }

        // Public detail/download must treat deactivated books as if they do not exist.
        // IncludeInactive is only set by the admin edit flow.
        if (!book.IsActive && !query.IncludeInactive)
        {
            return BookErrors.Validation.NotFound(query.Id);
        }

        var (average, count) = await _ratingRepository.GetRatingSummaryAsync(book.Id, cancellationToken);

        return new GetBookResult(book, average, count);
    }
}
