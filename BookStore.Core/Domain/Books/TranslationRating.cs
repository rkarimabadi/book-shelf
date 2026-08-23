using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Books;

/// <summary>A 1–5 star quality rating a user gives to a book (one per user per book).</summary>
public class TranslationRating : Entity
{
    public const int Min = 1;
    public const int Max = 5;

    public Guid UserId { get; private set; }
    public Guid BookId { get; private set; }
    public int Rating { get; private set; }
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private TranslationRating() { }

    public static ErrorOr<TranslationRating> Create(Guid userId, Guid bookId, int rating)
    {
        Guard.Against.Default(userId, nameof(userId));
        Guard.Against.Default(bookId, nameof(bookId));

        if (rating is < Min or > Max)
        {
            return TranslationRatingErrors.InvalidRating;
        }

        return new TranslationRating
        {
            UserId = userId,
            BookId = bookId,
            Rating = rating
        };
    }

    public ErrorOr<Success> Set(int rating)
    {
        if (rating is < Min or > Max)
        {
            return TranslationRatingErrors.InvalidRating;
        }

        Rating = rating;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success;
    }
}
