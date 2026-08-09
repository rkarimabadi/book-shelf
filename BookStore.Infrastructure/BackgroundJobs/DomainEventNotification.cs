using MediatR;

namespace BookStore.Infrastructure.BackgroundJobs;

public sealed record DomainEventNotification(object DomainEvent) : INotification;
