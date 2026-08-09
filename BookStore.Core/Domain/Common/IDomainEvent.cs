namespace BookStore.Core.Domain.Common;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}