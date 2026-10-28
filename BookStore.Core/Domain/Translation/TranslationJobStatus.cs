namespace BookStore.Core.Domain.Translation;

public enum TranslationJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>What the AI does with the book's text.</summary>
public enum TranslationMode
{
    /// <summary>Decide from the text: Persian text is edited, anything else is translated.</summary>
    Auto = 0,

    /// <summary>Translate a foreign-language book into natural Persian.</summary>
    Translate = 1,

    /// <summary>Edit a machine-translated Persian book into natural, publishable Persian.</summary>
    Edit = 2
}
