using BookStore.Application.Common.Interfaces;

namespace BookStore.Infrastructure.Authentication;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
