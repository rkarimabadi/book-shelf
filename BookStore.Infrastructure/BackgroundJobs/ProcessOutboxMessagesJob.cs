using System.Text.Json;
using BookStore.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace BookStore.Infrastructure.BackgroundJobs;

[DisallowConcurrentExecution]
public sealed class ProcessOutboxMessagesJob : IJob
{
    private const int BatchSize = 20;

    private readonly BookStoreDbContext _dbContext;
    private readonly IPublisher _publisher;

    public ProcessOutboxMessagesJob(BookStoreDbContext dbContext, IPublisher publisher)
    {
        _dbContext = dbContext;
        _publisher = publisher;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var messages = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOn)
            .Take(BatchSize)
            .ToListAsync(context.CancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type);
                if (type is null)
                {
                    message.Error = $"Type '{message.Type}' could not be resolved.";
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    continue;
                }

                var domainEvent = JsonSerializer.Deserialize(message.Content, type);
                if (domainEvent is null)
                {
                    message.Error = "Deserialization returned null.";
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    continue;
                }

                await _publisher.Publish(new DomainEventNotification(domainEvent), context.CancellationToken);

                message.ProcessedOnUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.Error = ex.ToString();
            }
        }

        await _dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
