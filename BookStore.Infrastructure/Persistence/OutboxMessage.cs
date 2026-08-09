namespace BookStore.Infrastructure.Persistence;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(Guid id, string type, string content, DateTime occurredOn)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredOn = occurredOn;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTime OccurredOn { get; private set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
}
