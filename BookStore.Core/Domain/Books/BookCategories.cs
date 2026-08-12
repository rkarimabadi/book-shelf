namespace BookStore.Core.Domain.Books;

/// <summary>
/// The fixed set of book categories (دسته/ژانر). Values are the Persian labels themselves —
/// the whole app is Persian-first, so the stored value, the API payload and the UI labels are
/// the same string (no code→label mapping layer anywhere).
/// </summary>
public static class BookCategories
{
    public const string Novel = "رمان";
    public const string Science = "علمی";
    public const string History = "تاریخی";
    public const string Philosophy = "فلسفه";
    public const string Poetry = "شعر و ادبیات";
    public const string Children = "کودک و نوجوان";
    public const string Religious = "مذهبی";
    public const string General = "متفرقه";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Novel,
        Science,
        History,
        Philosophy,
        Poetry,
        Children,
        Religious,
        General
    };

    /// <summary>
    /// The migration default for existing rows (books created before categories existed land in
    /// «متفرقه»); also the safe fallback for form defaults.
    /// </summary>
    public static bool IsValid(string? category) =>
        !string.IsNullOrWhiteSpace(category) && All.Contains(category);
}
