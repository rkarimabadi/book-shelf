using System.Collections.Concurrent;
using System.Threading.Channels;
using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Translation;

/// <summary>
/// Background queue that runs translation jobs one at a time (the chunks of a job run in
/// parallel inside <see cref="TranslationPipeline"/>). On startup it re-queues every job that
/// was queued or running when the app stopped; their finished chunks are reused.
/// </summary>
public sealed class TranslationWorker : BackgroundService, ITranslationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranslationWorker> _logger;

    public TranslationWorker(IServiceScopeFactory scopeFactory, ILogger<TranslationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(Guid jobId)
    {
        if (_pending.TryAdd(jobId, 0))
        {
            _channel.Writer.TryWrite(jobId);
        }
    }

    public void Cancel(Guid jobId)
    {
        if (_running.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITranslationJobRepository>();
            foreach (var id in await repository.GetActiveIdsAsync(stoppingToken))
            {
                Enqueue(id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not resume translation jobs.");
        }

        await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _pending.TryRemove(jobId, out _);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _running[jobId] = cts;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<TranslationPipeline>();
                await pipeline.RunAsync(jobId, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Cancelled by the admin (status already saved) or by shutdown (resumed next start).
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Translation job {JobId} crashed.", jobId);
                await TryFailAsync(jobId, "Internal: " + ex.Message);
            }
            finally
            {
                _running.TryRemove(jobId, out _);
            }
        }
    }

    private async Task TryFailAsync(Guid jobId, string message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITranslationJobRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var job = await repository.GetByIdAsync(jobId);
            if (job is not null && !job.Fail(message).IsError)
            {
                repository.Update(job);
                await unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark translation job {JobId} as failed.", jobId);
        }
    }
}
