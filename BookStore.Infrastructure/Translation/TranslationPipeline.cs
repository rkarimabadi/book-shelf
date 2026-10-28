using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookStore.Application.Common.Interfaces;
using BookStore.Application.Features.AiSettings.Common;
using BookStore.Core.Domain.Translation;
using BookStore.Infrastructure.Translation.Epub;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Translation;

/// <summary>
/// Runs one translation job end to end, following the persian-book-editorial workflow:
/// prepare (unpack + split into chunks) → book bible (names + style decisions) → every
/// content chunk in parallel → table of contents (copying the chapter headings) → finish
/// (rebuild the documents, RTL + Persian metadata, new cover, pack the EPUB).
/// Each finished chunk is saved in the job folder at once, so a stopped job resumes there.
/// Job errors are stored as "Code: message" so the UI can map the code to Persian.
/// </summary>
public sealed class TranslationPipeline
{
    private const int MaxAttempts = 3;
    private const int BibleSampleWords = 2500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AiConnectionResolver _resolver;
    private readonly IAiTextGenerator _generator;
    private readonly TranslationWorkspace _workspace;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranslationPipeline> _logger;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public TranslationPipeline(
        AiConnectionResolver resolver,
        IAiTextGenerator generator,
        TranslationWorkspace workspace,
        IServiceScopeFactory scopeFactory,
        ILogger<TranslationPipeline> logger)
    {
        _resolver = resolver;
        _generator = generator;
        _workspace = workspace;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private sealed record JobState(int ChunkWords, TranslationMode Mode);

    private sealed record ChunkResult(Dictionary<int, string> Lines, bool Fallback);

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await ReadJobAsync(jobId, cancellationToken);
        if (job is null || !job.IsActive)
        {
            return;
        }

        var resolved = await _resolver.ResolveAsync(cancellationToken);
        if (resolved.IsError)
        {
            await FailAsync(jobId, resolved.FirstError);
            return;
        }

        var settings = resolved.Value.Settings;
        var connection = resolved.Value.Connection;

        if (!File.Exists(_workspace.SourcePath(jobId)))
        {
            await FailAsync(jobId, Error.NotFound("TranslationJob.SourceMissing", "The uploaded EPUB is missing from the work folder."));
            return;
        }

        EpubBook book;
        await using (var source = File.OpenRead(_workspace.SourcePath(jobId)))
        {
            var loaded = EpubBook.Load(source);
            if (loaded.IsError)
            {
                await FailAsync(jobId, loaded.FirstError);
                return;
            }

            book = loaded.Value;
        }

        var state = await LoadOrCreateStateAsync(jobId, job, settings, book, cancellationToken);
        var plan = TranslationPlan.Build(book, state.ChunkWords);
        if (plan.Chunks.Count == 0)
        {
            await FailAsync(jobId, TranslationJobErrors.NoText);
            return;
        }

        Directory.CreateDirectory(_workspace.ChunkFolder(jobId));
        var done = plan.Chunks.Count(c => File.Exists(ChunkPath(jobId, c)));
        var fallbacks = plan.Chunks.Count(c => IsFallback(jobId, c));

        if (!await UpdateJobAsync(jobId, j => j.Start(plan.Chunks.Count, done, connection.Model), cancellationToken))
        {
            return;
        }

        var bible = await LoadOrCreateBibleAsync(jobId, job, state.Mode, book, plan, connection, cancellationToken);
        if (bible.IsError)
        {
            await FailAsync(jobId, bible.FirstError);
            return;
        }

        var system = TranslationPrompts.SystemPrompt(state.Mode);
        var context = new PromptContext(job, state.Mode, bible.Value, book, system, connection);

        // ------------------------------------------------ content chunks (parallel)
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Error? fatal = null;
        var cancelledByAdmin = false;
        using var gate = new SemaphoreSlim(settings.ParallelRequests);

        var tasks = plan.Chunks
            .Where(c => !c.IsToc && !File.Exists(ChunkPath(jobId, c)))
            .Select(async chunk =>
            {
                await gate.WaitAsync(linked.Token);
                try
                {
                    var blocks = plan.BlocksOf(chunk);
                    var result = await TranslateBlocksAsync(context, blocks, Label(plan, chunk), PreviousContext(plan, chunk), null, linked.Token);
                    if (result.IsError)
                    {
                        fatal ??= result.FirstError;
                        await linked.CancelAsync();
                        return;
                    }

                    await File.WriteAllTextAsync(ChunkPath(jobId, chunk),
                        ChunkResponse.Serialize(blocks, result.Value.Lines, result.Value.Fallback), linked.Token);

                    var completed = Interlocked.Increment(ref done);
                    var fallbackCount = result.Value.Fallback ? Interlocked.Increment(ref fallbacks) : Volatile.Read(ref fallbacks);
                    if (!await UpdateJobAsync(jobId, j => j.ReportProgress(completed, fallbackCount), CancellationToken.None))
                    {
                        cancelledByAdmin = true; // the job is no longer running (cancelled from the admin panel)
                        await linked.CancelAsync();
                    }
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        cancellationToken.ThrowIfCancellationRequested(); // shutdown: the job resumes on the next start
        if (cancelledByAdmin)
        {
            return;
        }

        if (fatal is not null)
        {
            await FailAsync(jobId, fatal.Value);
            return;
        }

        // ------------------------------------------------ table of contents (after the headings exist)
        var headings = HeadingMap(jobId, plan);
        foreach (var chunk in plan.Chunks.Where(c => c.IsToc && !File.Exists(ChunkPath(jobId, c))))
        {
            var blocks = plan.BlocksOf(chunk);
            var lines = new Dictionary<int, string>();
            foreach (var block in blocks.Where(b => EpubText.TokenMarks(b.Line).Count == 0))
            {
                if (headings.TryGetValue(HeadingKey(block.Line), out var persian))
                {
                    lines[block.Id] = persian;
                }
            }

            var rest = blocks.Where(b => !lines.ContainsKey(b.Id)).ToList();
            var fallback = false;
            if (rest.Count > 0)
            {
                var reference = string.Join("\n", headings.Take(300).Select(h => $"{h.Key} → {h.Value}"));
                var result = await TranslateBlocksAsync(context, rest, "table of contents", null, reference, cancellationToken);
                if (result.IsError)
                {
                    await FailAsync(jobId, result.FirstError);
                    return;
                }

                foreach (var (id, line) in result.Value.Lines)
                {
                    lines[id] = line;
                }

                fallback = result.Value.Fallback;
            }

            await File.WriteAllTextAsync(ChunkPath(jobId, chunk), ChunkResponse.Serialize(blocks, lines, fallback), cancellationToken);
            done++;
            if (fallback)
            {
                fallbacks++;
            }

            if (!await UpdateJobAsync(jobId, j => j.ReportProgress(done, fallbacks), CancellationToken.None))
            {
                return;
            }
        }

        // ------------------------------------------------ finish: rebuild + pack
        await AssembleAsync(jobId, job, book, plan, bible.Value, cancellationToken);

        await UpdateJobAsync(jobId, j => j.Complete(), CancellationToken.None);
        _logger.LogInformation("Translation job {JobId} finished ({Chunks} chunks, {Fallbacks} with fallback).",
            jobId, plan.Chunks.Count, fallbacks);
    }

    // ==================================================================== translation of one chunk

    private sealed record PromptContext(
        TranslationJob Job,
        TranslationMode Mode,
        TranslationBible Bible,
        EpubBook Book,
        string System,
        AiConnection Connection);

    /// <summary>
    /// Sends the blocks to the AI and checks the answer; problems go back to the model (up to
    /// <see cref="MaxAttempts"/> answers), after which the answer is repaired and flagged as a
    /// fallback. A truncated answer is retried as two halves. Provider errors are returned (fatal).
    /// </summary>
    private async Task<ErrorOr<ChunkResult>> TranslateBlocksAsync(
        PromptContext context,
        IReadOnlyList<EpubBlock> blocks,
        string label,
        string? previousContext,
        string? tocReference,
        CancellationToken cancellationToken)
    {
        var prompt = TranslationPrompts.ChunkPrompt(
            context.Mode,
            context.Book.Title,
            context.Book.Author,
            context.Bible,
            context.Job.Instructions,
            previousContext,
            tocReference,
            label,
            TranslationPlan.Format(blocks));

        var ids = blocks.Select(b => b.Id).ToHashSet();
        var lines = new Dictionary<int, string>();
        var feedback = string.Empty;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var reply = await _generator.GenerateAsync(context.Connection, context.System, prompt + feedback, cancellationToken);
            if (reply.IsError)
            {
                if (reply.FirstError.Code == "Ai.Truncated" && blocks.Count > 1)
                {
                    return await TranslateHalvesAsync(context, blocks, label, previousContext, tocReference, cancellationToken);
                }

                return reply.Errors;
            }

            lines = ChunkResponse.Parse(reply.Value).Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            var problems = ChunkResponse.Validate(lines, blocks);
            if (problems.Count == 0)
            {
                return new ChunkResult(lines, false);
            }

            _logger.LogInformation("Chunk {Label}: {Count} problems in answer {Attempt}.", label, problems.Count, attempt);
            feedback = TranslationPrompts.RetryNote(problems);
        }

        return new ChunkResult(ChunkResponse.Repair(lines, blocks), true);
    }

    private async Task<ErrorOr<ChunkResult>> TranslateHalvesAsync(
        PromptContext context,
        IReadOnlyList<EpubBlock> blocks,
        string label,
        string? previousContext,
        string? tocReference,
        CancellationToken cancellationToken)
    {
        // Split after a block that ends a sentence, as close to the middle as possible.
        var middle = blocks.Count / 2;
        var cut = Enumerable.Range(1, blocks.Count - 1)
            .OrderBy(i => (blocks[i - 1].EndsSentence ? 0 : 1000) + Math.Abs(i - middle))
            .First();

        var first = await TranslateBlocksAsync(context, blocks.Take(cut).ToList(), label + " (a)", previousContext, tocReference, cancellationToken);
        if (first.IsError)
        {
            return first.Errors;
        }

        var second = await TranslateBlocksAsync(context, blocks.Skip(cut).ToList(), label + " (b)", null, tocReference, cancellationToken);
        if (second.IsError)
        {
            return second.Errors;
        }

        var merged = new Dictionary<int, string>(first.Value.Lines);
        foreach (var (id, line) in second.Value.Lines)
        {
            merged[id] = line;
        }

        return new ChunkResult(merged, first.Value.Fallback || second.Value.Fallback);
    }

    private static string Label(TranslationPlan plan, TranslationChunk chunk)
    {
        var parts = plan.Chunks.Where(c => c.DocIndex == chunk.DocIndex).ToList();
        var docs = plan.Chunks.Select(c => c.DocIndex).Distinct().Count();
        return $"document {chunk.DocIndex + 1} of {docs}, part {parts.IndexOf(chunk) + 1} of {parts.Count}";
    }

    /// <summary>The last few source blocks before the chunk, so the model knows who is speaking.</summary>
    private static string? PreviousContext(TranslationPlan plan, TranslationChunk chunk)
    {
        var before = plan.Texts[chunk.DocIndex].Blocks.Where(b => b.Id < chunk.FirstId).TakeLast(3).ToList();
        return before.Count == 0 ? null : string.Join("\n", before.Select(b => EpubText.StripTokens(b.Line)));
    }

    // ==================================================================== bible

    private async Task<ErrorOr<TranslationBible>> LoadOrCreateBibleAsync(
        Guid jobId,
        TranslationJob job,
        TranslationMode mode,
        EpubBook book,
        TranslationPlan plan,
        AiConnection connection,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_workspace.JobFolder(jobId), "bible.json");
        if (File.Exists(path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<TranslationBible>(await File.ReadAllTextAsync(path, cancellationToken));
                if (saved is not null)
                {
                    return saved;
                }
            }
            catch (JsonException)
            {
            }
        }

        var content = plan.Chunks.Where(c => !c.IsToc).SelectMany(plan.BlocksOf).ToList();
        var sample = new StringBuilder();
        var words = 0;
        foreach (var block in content)
        {
            if (words >= BibleSampleWords)
            {
                break;
            }

            sample.Append(EpubText.StripTokens(block.Line)).Append('\n');
            words += block.Words;
        }

        var candidates = mode == TranslationMode.Translate
            ? HarvestNames(content.Select(b => EpubText.Visible(b.Line)))
            : [];

        var reply = await _generator.GenerateAsync(
            connection,
            TranslationPrompts.BibleSystemPrompt,
            TranslationPrompts.BiblePrompt(mode, book.Title, book.Author, candidates, sample.ToString()),
            cancellationToken);

        TranslationBible bible;
        if (reply.IsError)
        {
            if (reply.FirstError.Code is "Ai.Unauthorized" or "Ai.NotFound" or "Ai.BadRequest")
            {
                return reply.Errors;
            }

            _logger.LogWarning("Translation job {JobId}: no book bible ({Error}); continuing without one.", jobId, reply.FirstError.Description);
            bible = new TranslationBible(null, null, string.Empty, []);
        }
        else
        {
            bible = ParseBible(reply.Value);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(bible, JsonOptions), cancellationToken);
        return bible;
    }

    private static TranslationBible ParseBible(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return new TranslationBible(null, null, string.Empty, []);
        }

        try
        {
            using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
            var root = doc.RootElement;

            string? Read(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()?.Trim()
                    : null;

            var notes = root.TryGetProperty("notes", out var notesValue)
                ? notesValue.ValueKind switch
                {
                    JsonValueKind.String => notesValue.GetString() ?? string.Empty,
                    JsonValueKind.Array => string.Join("\n", notesValue.EnumerateArray().Select(n => n.ToString())),
                    _ => string.Empty
                }
                : string.Empty;

            var glossary = new List<GlossaryEntry>();
            if (root.TryGetProperty("glossary", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
                {
                    var source = item.TryGetProperty("source", out var s) ? s.GetString()?.Trim() : null;
                    var persian = item.TryGetProperty("fa", out var f) ? f.GetString()?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(persian))
                    {
                        glossary.Add(new GlossaryEntry(source, persian));
                    }
                }
            }

            return new TranslationBible(NullIfEmpty(Read("title")), NullIfEmpty(Read("author")), notes.Trim(), glossary);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new TranslationBible(null, null, string.Empty, []);
        }

        static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static readonly Regex MidSentenceCapitalRe = new(@"(?<=[a-z,;:'’] )[A-Z][a-z]+(?:[-'][A-Z]?[a-z]+)?", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NotNames =
        ["I", "The", "A", "An", "And", "But", "Or", "Mr", "Mrs", "Ms", "Dr", "Sir", "God", "Monday", "Tuesday",
         "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "January", "February", "March", "April", "May",
         "June", "July", "August", "September", "October", "November", "December", "English", "Chapter"];

    /// <summary>Capitalised words in mid-sentence that occur at least twice — likely names.</summary>
    private static List<(string Name, int Count)> HarvestNames(IEnumerable<string> texts) =>
        texts.SelectMany(t => MidSentenceCapitalRe.Matches(t).Select(m => m.Value))
            .Where(w => !NotNames.Contains(w))
            .GroupBy(w => w)
            .Select(g => (g.Key, g.Count()))
            .Where(g => g.Item2 >= 2)
            .OrderByDescending(g => g.Item2)
            .Take(150)
            .ToList();

    // ==================================================================== table of contents

    private static string HeadingKey(string line) =>
        Regex.Replace(EpubText.Visible(line), @"\s+", " ").Trim().ToLowerInvariant();

    /// <summary>Source heading → its Persian version, from the finished content chunks.</summary>
    private Dictionary<string, string> HeadingMap(Guid jobId, TranslationPlan plan)
    {
        var map = new Dictionary<string, string>();
        foreach (var chunk in plan.Chunks.Where(c => !c.IsToc))
        {
            var path = ChunkPath(jobId, chunk);
            if (!File.Exists(path))
            {
                continue;
            }

            var lines = ChunkResponse.Parse(File.ReadAllText(path));
            var blocks = plan.Texts[chunk.DocIndex].Blocks;
            foreach (var block in plan.BlocksOf(chunk))
            {
                var isTitleLike = block.IsHeading || (block.Id == blocks[0].Id && block.Words <= 12);
                if (!isTitleLike || !lines.TryGetValue(block.Id, out var line) || line.Trim() == EpubText.MergeCommand)
                {
                    continue;
                }

                var persian = Regex.Replace(EpubText.Visible(line.Replace(EpubText.SplitCommand, " ")), @"\s+", " ").Trim();
                var key = HeadingKey(block.Line);
                if (key.Length > 0 && persian.Length > 0)
                {
                    map.TryAdd(key, EpubText.NormalizeLine(persian));
                }
            }
        }

        return map;
    }

    // ==================================================================== assembling the book

    private async Task AssembleAsync(
        Guid jobId,
        TranslationJob job,
        EpubBook book,
        TranslationPlan plan,
        TranslationBible bible,
        CancellationToken cancellationToken)
    {
        foreach (var doc in book.Documents)
        {
            var edited = new Dictionary<int, string>();
            foreach (var chunk in plan.Chunks.Where(c => c.DocIndex == doc.Index))
            {
                foreach (var (id, line) in ChunkResponse.Parse(await File.ReadAllTextAsync(ChunkPath(jobId, chunk), cancellationToken)))
                {
                    edited[id] = line;
                }
            }

            if (edited.Count == 0)
            {
                continue;
            }

            var source = book.GetText(doc.Href);
            var rebuilt = EpubText.Rebuild(source, doc.Kind, edited, plan.Texts[doc.Index].Placeholders);
            var problem = EpubText.WellFormedError(rebuilt);
            if (problem is not null && EpubText.WellFormedError(source) is null)
            {
                // Only damage we caused counts; the document keeps its source text.
                _logger.LogWarning("Translation job {JobId}: {Doc} would not be well-formed ({Problem}); left unchanged.",
                    jobId, doc.Href, problem);
                continue;
            }

            book.SetText(doc.Href, rebuilt);
        }

        var title = job.Title ?? bible.Title;
        var author = job.Author ?? bible.Author;
        book.SetMetadata(title, author);

        foreach (var doc in book.Documents.Where(d => d.Kind != EpubDocKind.Ncx))
        {
            var content = book.GetText(doc.Href);
            var synced = EpubText.SyncTitle(content, title ?? string.Empty);
            if (synced != content && (EpubText.WellFormedError(synced) is null || EpubText.WellFormedError(content) is not null))
            {
                book.SetText(doc.Href, synced);
            }
        }

        book.FixRtl();

        var cover = _workspace.CoverPath(jobId);
        if (cover is not null)
        {
            book.ReplaceCover(await File.ReadAllBytesAsync(cover, cancellationToken), Path.GetExtension(cover));
        }

        var output = Path.Combine(_workspace.JobFolder(jobId), TranslationWorkspace.OutputFileName);
        var temp = output + ".tmp";
        await using (var file = File.Create(temp))
        {
            book.Save(file);
        }

        File.Move(temp, output, overwrite: true);
    }

    // ==================================================================== state + persistence helpers

    private async Task<JobState> LoadOrCreateStateAsync(
        Guid jobId,
        TranslationJob job,
        AiProviderSettings settings,
        EpubBook book,
        CancellationToken cancellationToken)
    {
        // The chunk size and resolved mode are fixed at the first run: chunk files are numbered by them.
        var path = Path.Combine(_workspace.JobFolder(jobId), "state.json");
        if (File.Exists(path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<JobState>(await File.ReadAllTextAsync(path, cancellationToken));
                if (saved is not null)
                {
                    return saved;
                }
            }
            catch (JsonException)
            {
            }
        }

        var mode = job.Mode;
        if (mode == TranslationMode.Auto)
        {
            var sample = string.Concat(book.Documents.Where(d => !d.IsToc).Take(6)
                .SelectMany(d => EpubText.ToText(book.GetText(d.Href), d.Kind).Blocks)
                .Select(b => EpubText.Visible(b.Line)));
            mode = EpubText.CountPersianLetters(sample) > EpubText.CountLatinLetters(sample)
                ? TranslationMode.Edit
                : TranslationMode.Translate;
        }

        var state = new JobState(settings.ChunkWords, mode);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        return state;
    }

    private string ChunkPath(Guid jobId, TranslationChunk chunk) =>
        Path.Combine(_workspace.ChunkFolder(jobId), $"{chunk.Number:D4}.txt");

    private bool IsFallback(Guid jobId, TranslationChunk chunk)
    {
        var path = ChunkPath(jobId, chunk);
        if (!File.Exists(path))
        {
            return false;
        }

        using var reader = new StreamReader(path);
        return reader.ReadLine() == ChunkResponse.FallbackHeader;
    }

    private async Task<TranslationJob?> ReadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITranslationJobRepository>().GetByIdAsync(jobId, cancellationToken);
    }

    /// <summary>
    /// Applies a change to a freshly loaded job in its own scope (chunks finish on several
    /// threads; a DbContext is not thread-safe). False when the job is gone or the change was
    /// refused — i.e. the job is no longer running.
    /// </summary>
    private async Task<bool> UpdateJobAsync(Guid jobId, Func<TranslationJob, ErrorOr<Success>> change, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITranslationJobRepository>();
            var job = await repository.GetByIdAsync(jobId, cancellationToken);
            if (job is null || change(job).IsError)
            {
                return false;
            }

            repository.Update(job);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private Task<bool> FailAsync(Guid jobId, Error error)
    {
        _logger.LogWarning("Translation job {JobId} failed: {Code} {Description}", jobId, error.Code, error.Description);
        return UpdateJobAsync(jobId, j => j.Fail($"{error.Code}: {error.Description}"), CancellationToken.None);
    }
}
