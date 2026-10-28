using System.Text;
using System.Text.RegularExpressions;
using BookStore.Infrastructure.Translation.Epub;

namespace BookStore.Infrastructure.Translation;

/// <summary>A run of consecutive blocks of one document that the AI handles in one request.</summary>
internal sealed record TranslationChunk(int Number, int DocIndex, int FirstId, int LastId, int Words, bool IsToc);

/// <summary>
/// The book split into chunks of about <c>chunkWords</c> words (port of epub_tool make_plan):
/// a chunk never ends on a block that stops mid-sentence (so ⟪merge⟫ never crosses chunks), a
/// tiny tail joins the previous chunk, and the table-of-contents documents come last.
/// </summary>
internal sealed class TranslationPlan
{
    public IReadOnlyList<EpubDocText> Texts { get; }
    public IReadOnlyList<TranslationChunk> Chunks { get; }

    private TranslationPlan(IReadOnlyList<EpubDocText> texts, IReadOnlyList<TranslationChunk> chunks)
    {
        Texts = texts;
        Chunks = chunks;
    }

    public static TranslationPlan Build(EpubBook book, int chunkWords)
    {
        var texts = book.Documents.Select(d => EpubText.ToText(book.GetText(d.Href), d.Kind)).ToList();
        var chunks = new List<(int Doc, int First, int Last, int Words, bool Toc)>();
        var toc = new List<(int Doc, int First, int Last, int Words, bool Toc)>();

        foreach (var doc in book.Documents)
        {
            var blocks = texts[doc.Index].Blocks;
            if (blocks.Count == 0)
            {
                continue;
            }

            if (doc.IsToc)
            {
                toc.Add((doc.Index, blocks[0].Id, blocks[^1].Id, blocks.Sum(b => b.Words), true));
                continue;
            }

            var parts = new List<List<EpubBlock>>();
            var current = new List<EpubBlock>();
            var words = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                current.Add(blocks[i]);
                words += blocks[i].Words;
                var more = i + 1 < blocks.Count;
                if (more && words >= chunkWords && (blocks[i].EndsSentence || words >= 1.5 * chunkWords))
                {
                    parts.Add(current);
                    current = [];
                    words = 0;
                }
            }

            if (current.Count > 0)
            {
                if (parts.Count > 0 && words < 0.3 * chunkWords)
                {
                    parts[^1].AddRange(current);
                }
                else
                {
                    parts.Add(current);
                }
            }

            chunks.AddRange(parts.Select(p => (doc.Index, p[0].Id, p[^1].Id, p.Sum(b => b.Words), false)));
        }

        var all = chunks.Concat(toc)
            .Select((c, i) => new TranslationChunk(i + 1, c.Doc, c.First, c.Last, c.Words, c.Toc))
            .ToList();

        return new TranslationPlan(texts, all);
    }

    public IReadOnlyList<EpubBlock> BlocksOf(TranslationChunk chunk) =>
        Texts[chunk.DocIndex].Blocks.Where(b => b.Id >= chunk.FirstId && b.Id <= chunk.LastId).ToList();

    public static string Format(IEnumerable<EpubBlock> blocks) =>
        string.Join("\n", blocks.Select(b => $"⟦{b.Id}⟧ {b.Line}"));
}

/// <summary>Reads, checks and (as a last resort) repairs the AI's answer for one chunk.</summary>
internal static class ChunkResponse
{
    private const string Digits = "0-9۰-۹٠-٩";

    /// <summary>Lines by block id. Tolerant of the usual slips: ⟦۱۲⟧, ⟦ 12 ⟧, bullets, wrapped lines, code fences.</summary>
    public static Dictionary<int, string> Parse(string response)
    {
        var lines = new Dictionary<int, string>();
        int? last = null;

        foreach (var raw in response.Split('\n'))
        {
            var s = raw.Trim().TrimStart('﻿').Trim();
            if (s.Length == 0 || s.StartsWith('#') || s.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            s = NormalizeMarks(Regex.Replace(s, @"^[-*•]\s+(?=⟦)", string.Empty));
            var m = EpubText.BlockLineRe.Match(s);
            if (m.Success)
            {
                var id = int.Parse(m.Groups[1].Value);
                lines[id] = m.Groups[2].Value.Trim();
                last = id;
            }
            else if (last is not null)
            {
                lines[last.Value] = (lines[last.Value] + " " + s).Trim();
            }
        }

        return lines;
    }

    private static string NormalizeMarks(string s)
    {
        s = Regex.Replace(s, $@"⟦\s*([{Digits}]+)\s*⟧", m => $"⟦{AsciiDigits(m.Groups[1].Value)}⟧");
        s = Regex.Replace(s, $@"⟨\s*(/?)\s*([{Digits}]+)\s*(/?)\s*⟩",
            m => $"⟨{m.Groups[1].Value}{AsciiDigits(m.Groups[2].Value)}{m.Groups[3].Value}⟩");
        return Regex.Replace(s, @"⟪\s*([A-Za-z]+)\s*[⟩⟫]", m => $"⟪{m.Groups[1].Value.ToLowerInvariant()}⟫");
    }

    private static string AsciiDigits(string s) =>
        new(s.Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            _ => c
        }).ToArray());

    /// <summary>Problems in the answer, phrased so they can be sent back to the model.</summary>
    public static List<string> Validate(IReadOnlyDictionary<int, string> lines, IReadOnlyList<EpubBlock> blocks) =>
        Check(new Dictionary<int, string>(lines), blocks, repair: false);

    /// <summary>
    /// Makes the answer safe to apply: missing or broken blocks fall back to the source line,
    /// mangled inline tokens are dropped (formatting lost, markup stays well-formed).
    /// </summary>
    public static Dictionary<int, string> Repair(IReadOnlyDictionary<int, string> lines, IReadOnlyList<EpubBlock> blocks)
    {
        var copy = new Dictionary<int, string>(lines);
        Check(copy, blocks, repair: true);
        return copy;
    }

    private static List<string> Check(Dictionary<int, string> lines, IReadOnlyList<EpubBlock> blocks, bool repair)
    {
        var problems = new List<string>();
        EpubBlock? previous = null;

        foreach (var block in blocks)
        {
            if (!lines.TryGetValue(block.Id, out var text) || text.Trim().Length == 0)
            {
                problems.Add($"⟦{block.Id}⟧ is missing or empty; write the whole block.");
                lines[block.Id] = block.Line;
                previous = block;
                continue;
            }

            text = text.Trim();
            if (text == EpubText.MergeCommand)
            {
                if (!block.IsFlow || previous is null || !previous.IsFlow)
                {
                    problems.Add($"⟦{block.Id}⟧: ⟪merge⟫ is only allowed for a paragraph that continues the paragraph above it in this chunk.");
                    lines[block.Id] = block.Line;
                }

                previous = block;
                continue;
            }

            if (EpubText.Visible(text).Trim().Length == 0)
            {
                problems.Add($"⟦{block.Id}⟧ has no text; write the whole block.");
                lines[block.Id] = block.Line;
            }
            else if (text.Contains(EpubText.SplitCommand, StringComparison.Ordinal) && !block.IsFlow)
            {
                problems.Add($"⟦{block.Id}⟧: ⟪split⟫ is only allowed in ordinary paragraphs, not in a heading, list item or table cell.");
                lines[block.Id] = text.Replace(EpubText.SplitCommand, " ");
            }

            var visible = EpubText.Visible(lines[block.Id]);
            if (EpubText.CountLatinLetters(EpubText.Visible(block.Line)) >= 20
                && EpubText.CountLatinLetters(visible) > EpubText.CountPersianLetters(visible))
            {
                problems.Add($"⟦{block.Id}⟧ is not in Persian; translate it.");
            }

            previous = block;
        }

        // Tokens are checked per group: a paragraph plus the blocks merged into it.
        for (var i = 0; i < blocks.Count; i++)
        {
            var head = blocks[i];
            if (lines[head.Id].Trim() == EpubText.MergeCommand)
            {
                continue;
            }

            var group = new List<EpubBlock> { head };
            while (i + 1 < blocks.Count && lines[blocks[i + 1].Id].Trim() == EpubText.MergeCommand)
            {
                group.Add(blocks[++i]);
            }

            var expected = group.SelectMany(b => EpubText.TokenMarks(b.Line)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            var line = lines[head.Id];
            var actual = EpubText.TokenMarks(line).OrderBy(t => t, StringComparer.Ordinal).ToList();

            if (!expected.SequenceEqual(actual) || !TokensBalanced(line))
            {
                problems.Add(expected.Count == 0
                    ? $"⟦{head.Id}⟧ must contain no ⟨n⟩ tokens."
                    : $"⟦{head.Id}⟧ must contain exactly these tokens, each once, with ⟨n⟩ before ⟨/n⟩: {string.Join(" ", expected)}");

                if (repair)
                {
                    var standalone = expected.Where(t => t.EndsWith("/⟩", StringComparison.Ordinal));
                    lines[head.Id] = (EpubText.StripTokens(line).Trim() + " " + string.Concat(standalone)).Trim();
                }
            }
        }

        return problems;
    }

    /// <summary>Every ⟨n⟩ is followed by its ⟨/n⟩ inside the same ⟪split⟫ part.</summary>
    private static bool TokensBalanced(string line)
    {
        foreach (var part in line.Split(EpubText.SplitCommand))
        {
            var open = new Stack<string>();
            foreach (Match m in EpubText.TokenRe.Matches(part))
            {
                var isClose = m.Groups[1].Value.Length > 0;
                var isStandalone = m.Groups[3].Value.Length > 0;
                if (isStandalone)
                {
                    continue;
                }

                if (!isClose)
                {
                    open.Push(m.Groups[2].Value);
                }
                else if (open.Count == 0 || open.Pop() != m.Groups[2].Value)
                {
                    return false;
                }
            }

            if (open.Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    public const string FallbackHeader = "# fallback";

    public static string Serialize(IReadOnlyList<EpubBlock> blocks, IReadOnlyDictionary<int, string> lines, bool fallback)
    {
        var sb = new StringBuilder();
        if (fallback)
        {
            sb.Append(FallbackHeader).Append('\n');
        }

        foreach (var block in blocks)
        {
            sb.Append('⟦').Append(block.Id).Append("⟧ ").Append(lines[block.Id]).Append('\n');
        }

        return sb.ToString();
    }
}
