using BookStore.Core.Domain.Books;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.Ratings.Queries.GetBookRating;

/// <summary>A book's rating summary plus the requesting user's own rating (null when anonymous or not rated).</summary>
public record GetBookRatingResult(
    double? Average,
    int Count,
    int? MyRating);

public record GetBookRatingQuery(Guid? UserId, Guid BookId) : IRequest<ErrorOr<GetBookRatingResult>>;

public class GetBookRatingQueryHandler : IRequestHandler<GetBookRatingQuery, ErrorOr<GetBookRatingResult>>
{
    private readonly ITranslationRatingRepository _ratingRepository;

    public GetBookRatingQueryHandler(ITranslationRatingRepository ratingRepository)
    {
        _ratingRepository = ratingRepository;
    }

    public async Task<ErrorOr<GetBookRatingResult>> Handle(GetBookRatingQuery query, CancellationToken cancellationToken)
    {
        var (average, count) = await _ratingRepository.GetRatingSummaryAsync(query.BookId, cancellationToken);

        int? ownRating = null;
        if (query.UserId is not null)
        {
            var own = await _ratingRepository.GetByUserAndBookAsync(query.UserId.Value, query.BookId, cancellationToken);
            ownRating = own?.Rating;
        }

        return new GetBookRatingResult(average, count, ownRating);
    }
}
