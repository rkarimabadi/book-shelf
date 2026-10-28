using System.Text;
using BookStore.Core.Domain.Translation;

namespace BookStore.Infrastructure.Translation;

/// <summary>Names and style decisions shared by every chunk of one book (the "book bible").</summary>
internal sealed record TranslationBible(
    string? Title,
    string? Author,
    string Notes,
    IReadOnlyList<GlossaryEntry> Glossary);

internal sealed record GlossaryEntry(string Source, string Persian);

/// <summary>
/// The prompts sent to the AI. The rules are a condensed form of the persian-book-editorial
/// skill's editor guide (references/editor-guide.md): keep the story, the names and the
/// author's voice; natural Persian narration; colloquial written dialogue; strict block/token format.
/// </summary>
internal static class TranslationPrompts
{
    private const string FormatRules = """
        FORMAT (strict — a program reads your answer)
        - The text comes as blocks, one per line: ⟦id⟧ text. Answer with exactly the same ⟦id⟧ blocks, in the same order, one block per line. Write nothing else: no preface, no comments, no code fences.
        - ⟨n⟩ … ⟨/n⟩ wrap formatted words (italics, bold, links); ⟨n/⟩ stands for an image, a line break or a marker. Keep every token exactly once in its block, around the Persian words that correspond, with ⟨n⟩ before ⟨/n⟩.
        - A paragraph that stops mid-sentence and is continued by the next block: move all the words into the first block and write the next one as `⟦id⟧ ⟪merge⟫`. Two speakers in one paragraph: put ⟪split⟫ where the second speaker starts. Use ⟪merge⟫ and ⟪split⟫ only for ordinary paragraphs, never for headings, list items or table cells.
        - Plain Persian text only: no notes, brackets, alternatives, transliteration hints or Latin letters (write names and brands in Persian letters: iPhone → آیفون).
        """;

    private const string StyleRules = """
        NEVER CHANGE
        - The story: every event, detail and piece of information, in the same order. Add nothing and drop nothing. Every scene is rendered faithfully at the intensity the author wrote it; do not soften, censor or embellish.
        - Names, places and setting: never replace them with Iranian ones. Write every name exactly as the NAMES list says; a name must never turn into an ordinary word (Olive → «اولیو», not «زیتون»; Hope → «هوپ», not «امید») when it stands for the person.
        - The narration: first person stays first person, present stays present, past stays past.
        - The author's voice: dry stays dry, ironic stays ironic. Add no drama, exclamation marks, explanations or feelings the author did not write.
        - Chapter titles: write them in the one form the BOOK BIBLE gives, the same way in every chapter («فصل بیست‌وهفتم», never «فصل ۲۷» or «فصل بیست‌وهفت»).

        NATURAL PERSIAN NARRATION
        - Verb at the end; no extra «او/من»; no English «یک» (یک زن با یک کیف → زنی با کیفی); no passive with «توسط» (use the active voice); no «در حال … بود» (روی صندلی نشسته بود); «تصمیم گرفت» not «یک تصمیم ساخت»; «توانست» not «قادر بود»; «کتابش» rather than «کتاب او»; plain words instead of bookish ones («است»، «برای»، «گرفت» — not «می‌باشد»، «جهت»، «اخذ کرد»).
        - «او» means both he and she: in a scene with two or more people use the name or a role («مرد»، «پیرزن») every two or three sentences and whenever the person acting changes.
        - تو/شما: follow the BOOK BIBLE; otherwise strangers, elders and bosses get شما, friends, family and lovers get تو.
        - Idioms get their Persian equivalent, never a literal rendering (his heart sank → دلش هُرّی ریخت; piece of cake → مثل آب خوردن بود; rolled her eyes → پشت چشم نازک کرد; I'm fine → خوبم). Explain an unfamiliar thing inside the sentence, no footnotes. Fahrenheit becomes Celsius, miles become kilometres where it reads naturally.
        - Inside a paragraph you may merge, split and reorder sentences (context first, key information last) as long as the order of events stays. Length is usually 80–100% of a wordy literal version; under 70% means something was dropped.

        DIALOGUE: COLLOQUIAL WRITTEN PERSIAN
        - Narration stays standard written Persian; letters and documents keep the writer's register; speech is colloquial: می‌رم، می‌خوام، نمی‌دونم، می‌گه، می‌تونم، می‌شه، میام، بذار، بشین، اگه، مگه، دیگه، خونه، اومد، اونجا، اون، اونا، اینا، یه، بهم، باهام، ازم، برام، چی، کی، «رو» for «را»، «ـه» for «است» (خوبه، کجاست)، «ـن» for «ـند»، «ـین» for «ـید»؛ drop «آیا»؛ «خواهم رفت» → «می‌رم».
        - Never use «ـه» as an ezafe: «خونه‌ی ما», not «خونه ما».
        - Speaker tag first («الینور گفت: «امشب میام.»»), or no tag when the speaker is clear, or an action before the line. Final punctuation goes inside the guillemets: «کی میای؟»

        SPELLING
        - Half-space (نیم‌فاصله): می‌رود، نمی‌دانم، کتاب‌ها، بزرگ‌تر، خسته‌ام، هیچ‌وقت. Ezafe after «ه»: «خانه‌ی ما».
        - «» for speech, «…» for an ellipsis, ، ؛ ؟ with no space before them. No dashes in narration; a dash only ends cut-off speech («داشتم می‌گفتم که—»).
        - Ages and small numbers in words («چهل‌وسه سالش بود»).

        Before you answer, read each paragraph at speaking speed; if you stumble, fix that sentence.
        """;

    public static string SystemPrompt(TranslationMode mode)
    {
        var task = mode == TranslationMode.Edit
            ? """
              You are a senior Persian literary editor. You edit chunks of a machine-translated Persian book (Google Translate, DeepL, GPT, …) so that it reads as if it had been written in Persian, without changing the story. When a sentence is strange, guess the English behind it and write what it meant (من جریمه هستم → خوبم; بانک رودخانه → کنار رودخانه; یک تاریخ دارم → قرار دارم).
              """
            : """
              You are a senior literary translator and editor. You translate chunks of a book into natural, publishable Persian (Farsi) that reads as if it had been written in Persian, without changing the story.
              """;

        return task + "\n\n" + FormatRules + "\n\n" + StyleRules;
    }

    public static string ChunkPrompt(
        TranslationMode mode,
        string bookTitle,
        string bookAuthor,
        TranslationBible bible,
        string? instructions,
        string? previousContext,
        string? tocReference,
        string chunkLabel,
        string chunkText)
    {
        var sb = new StringBuilder();
        sb.Append("BOOK: ").Append(bookTitle);
        if (!string.IsNullOrWhiteSpace(bookAuthor))
        {
            sb.Append(" — ").Append(bookAuthor);
        }

        sb.Append("\n\n");

        if (!string.IsNullOrWhiteSpace(bible.Notes))
        {
            sb.Append("BOOK BIBLE (decisions every translator of this book follows):\n").Append(bible.Notes.Trim()).Append("\n\n");
        }

        var names = bible.Glossary
            .Where(g => mode == TranslationMode.Edit || chunkText.Contains(g.Source, StringComparison.OrdinalIgnoreCase))
            .Take(80)
            .ToList();
        if (names.Count > 0)
        {
            sb.Append("NAMES (write exactly like this):\n");
            foreach (var entry in names)
            {
                sb.Append(entry.Source).Append(" → ").Append(entry.Persian).Append('\n');
            }

            sb.Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            sb.Append("EXTRA INSTRUCTIONS FROM THE PUBLISHER:\n").Append(instructions.Trim()).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(tocReference))
        {
            sb.Append("This chunk is the TABLE OF CONTENTS. Every entry that names a chapter must repeat that chapter's Persian heading word for word. The book's headings (source → Persian):\n")
              .Append(tocReference).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(previousContext))
        {
            sb.Append("PREVIOUS TEXT (context only — do not include it in your answer):\n").Append(previousContext).Append("\n\n");
        }

        sb.Append(mode == TranslationMode.Edit ? "TEXT TO EDIT" : "TEXT TO TRANSLATE")
          .Append(" (").Append(chunkLabel).Append("):\n")
          .Append(chunkText);

        return sb.ToString();
    }

    public static string RetryNote(IEnumerable<string> problems) =>
        "\n\nYOUR PREVIOUS ANSWER HAD THESE PROBLEMS — answer again with the whole chunk, fixing them:\n- " +
        string.Join("\n- ", problems.Take(25));

    public const string BibleSystemPrompt = """
        You prepare the "book bible" for a team of literary translators who will translate (or edit a machine translation of) a book into natural Persian, chunk by chunk. Your decisions keep fifty translators consistent. Answer with ONE JSON object and nothing else.
        """;

    public static string BiblePrompt(
        TranslationMode mode,
        string sourceTitle,
        string sourceAuthor,
        IReadOnlyList<(string Name, int Count)> candidates,
        string sample)
    {
        var sb = new StringBuilder();
        sb.Append(mode == TranslationMode.Edit
            ? "The book is a machine translation into Persian that will be edited.\n"
            : "The book will be translated into Persian.\n");
        sb.Append("Title: ").Append(sourceTitle).Append('\n');
        sb.Append("Author: ").Append(sourceAuthor).Append("\n\n");

        if (candidates.Count > 0)
        {
            sb.Append("Words that look like proper names (with how often they occur):\n");
            sb.Append(string.Join(", ", candidates.Select(c => $"{c.Name} ({c.Count})"))).Append("\n\n");
        }

        sb.Append("Opening of the book:\n").Append(sample).Append("\n\n");
        sb.Append("""
            Return this JSON:
            {
              "title": "the book title in Persian (keep it short and natural)",
              "author": "the author's name in Persian letters",
              "notes": "5–10 short lines in Persian: who narrates (person, tense), the author's voice/tone, the main characters and how they relate, who says تو or شما to whom, and exactly how chapter headings are written (one form for the whole book, e.g. «فصل اول»، «فصل بیست‌وهفتم»)",
              "glossary": [ { "source": "name as written in the book", "fa": "Persian spelling" } ]
            }
            The glossary lists people, places and invented things only (no ordinary words), with the standard Persian transliteration; for an edit, list the names the machine translated as ordinary words (e.g. زیتون for Olive) with the correct name.
            """);

        return sb.ToString();
    }
}
