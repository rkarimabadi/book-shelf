using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace BookStore.Infrastructure.Translation.Epub;

/// <summary>Kind of an EPUB text document; each kind has its own set of editable block tags.</summary>
internal enum EpubDocKind
{
    Xhtml,
    Nav,
    Ncx
}

/// <summary>One editable leaf block of a document, as a single line with inline tokens.</summary>
internal sealed record EpubBlock(int Id, string Tag, string Line, int Words, bool EndsSentence)
{
    public bool IsFlow => EpubText.FlowTags.Contains(Tag);

    public bool IsHeading => Tag.Length == 2 && Tag[0] == 'h' && char.IsDigit(Tag[1]);
}

/// <summary>A document turned into editable lines; <see cref="Placeholders"/> maps token ids to the original tags.</summary>
internal sealed record EpubDocText(IReadOnlyList<EpubBlock> Blocks, IReadOnlyDictionary<int, string[]> Placeholders);

/// <summary>
/// Text extraction and rebuilding for EPUB documents — a C# port of the block/token engine of
/// the persian-book-editorial <c>epub_tool.py</c>. Every leaf block (p, h1–h6, li, …) becomes one
/// line; inline tags become numbered tokens (⟨n⟩ … ⟨/n⟩, ⟨n/⟩) so the AI only ever sees plain
/// text, and the original markup is put back verbatim when the edited lines are rendered.
/// </summary>
internal static class EpubText
{
    public const char Zwnj = '\u200C';
    public const string MergeCommand = "⟪merge⟫";
    public const string SplitCommand = "⟪split⟫";

    private const string FaLetters = "\u0620-\u06D3";
    private const string FaRange = "\u0600-\u06FF\uFB50-\uFDFF\uFE70-\uFEFF";
    private const string DigitsFa = "۰۱۲۳۴۵۶۷۸۹";
    private const string DigitsAr = "٠١٢٣٤٥٦٧٨٩";
    private const string Nb = @"(?<![\w\u200C])";
    private const string Na = @"(?![\w\u200C])";
    private const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static readonly HashSet<string> FlowTags = ["p", "div", "blockquote"];
    private static readonly HashSet<string> VoidTags = ["br", "img", "hr", "wbr", "input", "meta", "link", "source"];
    private const string TerminalChars = ".!?؟…»:\"'”)*]—–-";

    private static readonly Regex XhtmlBlockRe = BlockRegex("p|h[1-6]|li|dt|dd|figcaption|caption|td|th|blockquote|div");
    private static readonly Regex NavBlockRe = BlockRegex("h[1-6]|p|a|span");
    private static readonly Regex NcxBlockRe = BlockRegex("text");

    public static readonly Regex BlockLineRe = new(@"^⟦(\d+)⟧[ \t]?(.*)$", RegexOptions.CultureInvariant);
    public static readonly Regex TokenRe = new(@"⟨(/?)(\d+)(/?)⟩", RegexOptions.CultureInvariant);
    private static readonly Regex AnyMarkRe = new(@"(⟨/?\d+/?⟩|⟪[^⟫⟩]*[⟩⟫])", RegexOptions.CultureInvariant);
    private static readonly Regex OkMarkRe = new(@"⟪\s*ok\s*[⟩⟫]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TagSplitRe = new(@"(<!--.*?-->|<[^>]+>)", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex TagNameRe = new(@"^<\s*(/)?\s*([A-Za-z][\w:.-]*)", RegexOptions.CultureInvariant);
    private static readonly Regex UnwrapSpanRe = new(
        "^<span(?:\\s+(?:xml:lang|lang|dir)\\s*=\\s*(?:\"[^\"]*\"|'[^']*'))*\\s*>$", Opts);
    private static readonly Regex IdAttrRe = new("\\s((?:xml:)?id)\\s*=\\s*(\"[^\"]*\"|'[^']*')", RegexOptions.CultureInvariant);
    private static readonly Regex LetterRe = new(@"[^\W\d_]", RegexOptions.CultureInvariant);

    private static Regex BlockRegex(string tags) => new(
        $@"<(?<tag>{tags})(?<attrs>(?:\s[^<>]*?)?)(?<!/)>" +
        $@"(?<inner>(?:(?!<(?:{tags})[\s/>])(?!</\k<tag>\s*>).)*?)" +
        @"</\k<tag>\s*>",
        Opts | RegexOptions.Compiled);

    private static Regex BlockRegexFor(EpubDocKind kind) => kind switch
    {
        EpubDocKind.Nav => NavBlockRe,
        EpubDocKind.Ncx => NcxBlockRe,
        _ => XhtmlBlockRe
    };

    // ------------------------------------------------------------------ small helpers

    public static string StripTags(string inner) => WebUtility.HtmlDecode(TagSplitRe.Replace(inner, string.Empty));

    public static bool HasLetters(string inner) => LetterRe.IsMatch(StripTags(inner));

    public static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => LetterRe.IsMatch(w));

    /// <summary>Text of a line without tokens or commands.</summary>
    public static string Visible(string line) => AnyMarkRe.Replace(line, string.Empty);

    /// <summary>All token marks of a line, e.g. ⟨3⟩, ⟨/3⟩, ⟨5/⟩, in order.</summary>
    public static List<string> TokenMarks(string line) => TokenRe.Matches(line).Select(m => m.Value).ToList();

    public static string StripTokens(string line) => TokenRe.Replace(line, string.Empty);

    public static int CountPersianLetters(string text) => text.Count(c => c is >= '\u0620' and <= '\u06D3');

    public static int CountLatinLetters(string text) => text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    /// <summary>Editable leaf blocks of a document, in order (ids are 1-based positions).</summary>
    public static List<Match> FindBlocks(string content, EpubDocKind kind) =>
        BlockRegexFor(kind).Matches(content).Where(m => HasLetters(m.Groups["inner"].Value)).ToList();

    public static string EscapeText(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Sets attribute <paramref name="name"/> on a start-tag string.</summary>
    public static string SetAttr(string tag, string name, string value)
    {
        var pattern = new Regex("(?<=\\s)" + Regex.Escape(name) + "\\s*=\\s*(\"[^\"]*\"|'[^']*')", RegexOptions.CultureInvariant);
        if (pattern.IsMatch(tag))
        {
            return pattern.Replace(tag, $"{name}=\"{value}\"", 1);
        }

        var end = tag.TrimEnd();
        var close = end.EndsWith("/>", StringComparison.Ordinal) ? "/>" : ">";
        return end[..^close.Length].TrimEnd() + $" {name}=\"{value}\"" + close;
    }

    // ------------------------------------------------------------------ extraction

    public static EpubDocText ToText(string content, EpubDocKind kind)
    {
        var placeholders = new Dictionary<int, string[]>();
        var counter = 0;
        var blocks = new List<EpubBlock>();

        foreach (var (match, index) in FindBlocks(content, kind).Select((m, i) => (m, i + 1)))
        {
            var line = InnerToLine(match.Groups["inner"].Value, placeholders, () => ++counter);
            var visible = Visible(line).Trim();
            blocks.Add(new EpubBlock(
                index,
                match.Groups["tag"].Value.ToLowerInvariant(),
                line,
                WordCount(visible),
                visible.Length > 0 && TerminalChars.Contains(visible[^1])));
        }

        return new EpubDocText(blocks, placeholders);
    }

    /// <summary>Turns a block's inner XHTML into one clean line; meaningless lang/dir spans vanish.</summary>
    private static string InnerToLine(string inner, Dictionary<int, string[]> placeholders, Func<int> nextId)
    {
        var output = new StringBuilder();
        var stack = new List<(string Name, int? Id)>();
        var open = new Dictionary<int, string>();

        void Standalone(string part)
        {
            var id = nextId();
            placeholders[id] = [part];
            output.Append($"⟨{id}/⟩");
        }

        foreach (var part in TagSplitRe.Split(inner))
        {
            if (part.Length == 0)
            {
                continue;
            }

            if (!part.StartsWith('<'))
            {
                output.Append(WebUtility.HtmlDecode(part).Replace('\u00A0', ' '));
                continue;
            }

            if (part.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            var m = TagNameRe.Match(part);
            if (!m.Success)
            {
                Standalone(part);
                continue;
            }

            var closing = m.Groups[1].Success && m.Groups[1].Value.Length > 0;
            var name = m.Groups[2].Value.ToLowerInvariant();

            if (closing)
            {
                if (stack.Count > 0 && stack[^1].Name == name)
                {
                    var (_, id) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    if (id is not null)
                    {
                        placeholders[id.Value] = [open[id.Value], part];
                        output.Append($"⟨/{id}⟩");
                    }
                }
                else
                {
                    Standalone(part);
                }

                continue;
            }

            if (part.TrimEnd().EndsWith("/>", StringComparison.Ordinal) || VoidTags.Contains(name))
            {
                Standalone(part);
                continue;
            }

            if (name == "span" && UnwrapSpanRe.IsMatch(part))
            {
                stack.Add((name, null));
                continue;
            }

            var newId = nextId();
            open[newId] = part;
            placeholders[newId] = [part];
            stack.Add((name, newId));
            output.Append($"⟨{newId}⟩");
        }

        var line = Regex.Replace(output.ToString(), @"\s+", " ").Trim();

        // An open tag that never closed stays a standalone token.
        foreach (var (_, id) in stack)
        {
            if (id is not null)
            {
                line = line.Replace($"⟨{id}⟩", $"⟨{id}/⟩");
            }
        }

        return line;
    }

    // ------------------------------------------------------------------ rebuilding

    /// <summary>
    /// Writes edited lines back into the document. <paramref name="edited"/> maps block ids to
    /// their new line (absent ids keep the original markup); a line of just ⟪merge⟫ folds the
    /// block into the one above, ⟪split⟫ inside a flow block starts a new element.
    /// </summary>
    public static string Rebuild(
        string content,
        EpubDocKind kind,
        IReadOnlyDictionary<int, string> edited,
        IReadOnlyDictionary<int, string[]> placeholders)
    {
        var blocks = FindBlocks(content, kind);
        var carried = new List<string>(); // ids of merged-away blocks: links may point at them
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        for (var index = blocks.Count; index >= 1; index--)
        {
            if (!edited.TryGetValue(index, out var text))
            {
                continue;
            }

            var m = blocks[index - 1];
            text = text.Trim();
            var start = m.Index;
            var end = m.Index + m.Length;
            var attrs = m.Groups["attrs"].Value;

            if (text == MergeCommand)
            {
                carried.InsertRange(0, IdAttrRe.Matches(attrs).Select(a => $"<span {a.Groups[1].Value}={a.Groups[2].Value}></span>"));

                var mergeLineStart = content.LastIndexOf('\n', Math.Max(start - 1, 0)) + 1;
                if (mergeLineStart <= start && content[mergeLineStart..start].Trim().Length == 0)
                {
                    start = mergeLineStart;
                }

                while (end < content.Length && content[end] is ' ' or '\t')
                {
                    end++;
                }

                if (string.CompareOrdinal(content, end, "\r\n", 0, 2) == 0)
                {
                    end += 2;
                }
                else if (end < content.Length && content[end] == '\n')
                {
                    end++;
                }

                content = content[..start] + content[end..];
                continue;
            }

            var lineStart = content.LastIndexOf('\n', Math.Max(start - 1, 0)) + 1;
            var indent = lineStart <= start && content[lineStart..start].Trim().Length == 0 ? content[lineStart..start] : string.Empty;
            var tag = m.Groups["tag"].Value;

            var elements = new List<string>();
            var parts = text.Split(SplitCommand);
            for (var i = 0; i < parts.Length; i++)
            {
                var elementAttrs = i == 0 ? attrs : IdAttrRe.Replace(attrs, string.Empty); // ids must stay unique
                var inner = Render(parts[i].Trim(), placeholders);
                if (i == 0 && carried.Count > 0)
                {
                    inner = string.Concat(carried) + inner;
                    carried.Clear();
                }

                elements.Add($"<{tag}{elementAttrs}>{inner}</{tag}>");
            }

            content = content[..start] + string.Join(newline + indent, elements) + content[end..];
        }

        return content;
    }

    private static string Render(string text, IReadOnlyDictionary<int, string[]> placeholders)
    {
        text = NormalizeLine(OkMarkRe.Replace(text, string.Empty));
        var output = new StringBuilder();
        foreach (var part in Regex.Split(text, @"(⟨/?\d+/?⟩)"))
        {
            var tm = TokenRe.Match(part);
            if (tm.Success && tm.Length == part.Length
                && placeholders.TryGetValue(int.Parse(tm.Groups[2].Value), out var tags))
            {
                var closing = tm.Groups[1].Value.Length > 0;
                output.Append(closing ? tags[^1] : tags[0]);
            }
            else if (part.Length > 0)
            {
                output.Append(EscapeText(part));
            }
        }

        return output.ToString();
    }

    // ------------------------------------------------------------------ Persian normalization

    private static readonly string[] PresentStems =
        ("رو ر شو ش کن تون توان خواه خوا دون دان گوی گ آی آ ده د زن بین گیر گذار ذار خور " +
         "پرس فهم رس نشین شین کش گرد ریز ساز سوز خند گری پوش نویس خوان خون شناس ترس لرز " +
         "چرخ افت انداز آور بر بند شکن خواب بوس پز بار").Split(' ');

    private static readonly string[] PresentEndings = "م ی د ه یم ید ین ن ند".Split(' ');

    private static readonly string[] PastStems =
        ("رفت شد کرد گفت آمد اومد خواست دانست دونست توانست تونست داد زد دید گرفت گذاشت ذاشت " +
         "خورد پرسید فهمید رسید نشست کشید دوید گشت ریخت ساخت سوخت خندید گریست پوشید نوشت " +
         "خواند خوند شناخت ترسید لرزید چرخید افتاد انداخت داشت آورد برد بست شکست خوابید " +
         "بوسید پخت بارید").Split(' ');

    private static readonly string[] PastEndings = ["", "م", "ی", "یم", "ید", "ین", "ند", "ن"];

    private static readonly HashSet<string> NotVerbs = ["ان", "رد", "دون", "دان"]; // میان, میرد, میدون, میدان

    private static readonly Regex MiJoinedRe = new(
        Nb + "(ن?)می(?=(?:" +
        string.Join("|", PresentStems.SelectMany(s => PresentEndings.Select(e => s + e))
            .Concat(PastStems.SelectMany(s => PastEndings.Select(e => s + e)))
            .Distinct()
            .Where(t => !NotVerbs.Contains(t))
            .OrderByDescending(t => t.Length)) +
        ")" + Na + ")",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MiSpacedRe = new(Nb + "(ن?)می +(?=[" + FaLetters + "])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string ToPersianDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
            {
                chars[i] = DigitsFa[chars[i] - '0'];
            }
            else if (DigitsAr.IndexOf(chars[i]) is var ar and >= 0)
            {
                chars[i] = DigitsFa[ar];
            }
        }

        return new string(chars);
    }

    private static string NormalizeSegment(string s)
    {
        s = s.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9').Replace('\u0649', '\u06CC');
        s = Regex.Replace(s, "[" + DigitsAr + "]+", m => ToPersianDigits(m.Value));
        s = Regex.Replace(s, "(?<![A-Za-z0-9])[0-9]+(?![A-Za-z])", m => ToPersianDigits(m.Value));
        s = s.Replace("\u06C0", "ه\u200Cی").Replace("ه\u0654", "ه\u200Cی");
        s = MiSpacedRe.Replace(s, m => m.Groups[1].Value + "می" + Zwnj);
        s = MiJoinedRe.Replace(s, m => m.Groups[1].Value + "می" + Zwnj);
        s = Regex.Replace(s, "(?<=[" + FaLetters + "]) (هایی|هایش|هایم|هایت|هایمان|هایتان|هایشان)" + Na, Zwnj + "$1");
        s = Regex.Replace(s, "(?<=[" + FaLetters + "]) (های)(?= [" + FaLetters + "])", Zwnj + "$1");
        s = Regex.Replace(s, "\u200C{2,}", "\u200C");
        s = Regex.Replace(s, " ?\u200C ?", m => m.Value == "\u200C" ? "\u200C" : " ");
        return s;
    }

    /// <summary>Spelling clean-up of a finished Persian line (tokens and commands untouched).</summary>
    public static string NormalizeLine(string line)
    {
        // Quotes are paired over the whole line (tokens never contain quotes).
        if (!line.Contains('«'))
        {
            line = line.Replace('“', '«').Replace('”', '»');
            var quotes = line.Count(c => c == '"');
            if (quotes > 0 && quotes % 2 == 0)
            {
                var pieces = line.Split('"');
                var sb = new StringBuilder(pieces[0]);
                for (var i = 1; i < pieces.Length; i++)
                {
                    sb.Append(i % 2 == 1 ? '«' : '»').Append(pieces[i]);
                }

                line = sb.ToString();
            }
        }

        line = Regex.Replace(line, @"\.\s?\.\s?\.", "…");
        var parts = Regex.Split(line, @"(⟨/?\d+/?⟩|⟪[^⟫]*⟫)");
        line = string.Concat(parts.Select((p, i) => i % 2 == 1 ? p : NormalizeSegment(p)));

        // ASCII punctuation after Persian text (a token may sit in between).
        line = Regex.Replace(line, "(?<=[" + FaRange + "»)⟩]) *\\?", "؟");
        line = Regex.Replace(line, "(?<=[" + FaRange + "»)⟩]) *,", "،");
        line = Regex.Replace(line, "(?<=[" + FaRange + "»)⟩]) *;", "؛");
        line = Regex.Replace(line, @"[ \t]+([،؛؟!.:…»)])", "$1");
        line = Regex.Replace(line, @"([«(])[ \t]+", "$1");
        line = Regex.Replace(line, "([،؛])(?=[^\\s»)⟨⟩⟪\\d" + DigitsFa + "])", "$1 ");
        return Regex.Replace(line, @"[ \t]{2,}", " ").Trim();
    }

    // ------------------------------------------------------------------ document-level helpers

    /// <summary>Makes &lt;head&gt;&lt;title&gt; follow the document's first heading.</summary>
    public static string SyncTitle(string content, string fallbackTitle)
    {
        var t = Regex.Match(content, @"(<title\b[^>]*>)(.*?)(</title\s*>)", Opts);
        if (!t.Success)
        {
            return content;
        }

        var h = Regex.Match(content, @"<(h[1-3])\b[^>]*>(.*?)</\1\s*>", Opts);
        string text;
        if (h.Success)
        {
            var inner = Regex.Replace(h.Groups[2].Value, @"<br\b[^>]*>", " ", Opts);
            text = string.Join(' ', StripTags(inner).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        else if (Regex.IsMatch(t.Groups[2].Value, "[A-Za-z]"))
        {
            text = fallbackTitle;
        }
        else
        {
            return content;
        }

        text = EscapeText(text);
        if (text.Length == 0 || text == t.Groups[2].Value.Trim())
        {
            return content;
        }

        return content[..t.Groups[2].Index] + text + content[(t.Groups[2].Index + t.Groups[2].Length)..];
    }

    /// <summary>Null when the document is well-formed XML, otherwise the parser's message.</summary>
    public static string? WellFormedError(string content)
    {
        var xml = Regex.Replace(content, "&([A-Za-z][A-Za-z0-9]*);", m =>
        {
            var name = m.Groups[1].Value;
            if (name is "amp" or "lt" or "gt" or "quot" or "apos")
            {
                return m.Value;
            }

            var decoded = WebUtility.HtmlDecode(m.Value);
            return decoded == m.Value
                ? m.Value
                : string.Concat(decoded.EnumerateRunes().Select(r => $"&#{r.Value};"));
        });

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
            }

            return null;
        }
        catch (XmlException ex)
        {
            return ex.Message;
        }
    }
}
