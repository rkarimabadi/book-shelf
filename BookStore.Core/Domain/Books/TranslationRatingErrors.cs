using ErrorOr;

namespace BookStore.Core.Domain.Books;

public static class TranslationRatingErrors
{
    public static Error InvalidRating =>
        Error.Validation("TranslationRating.InvalidRating", $"Rating must be between {TranslationRating.Min} and {TranslationRating.Max}.");
}
