using Ardalis.GuardClauses;

namespace BookStore.Core.Domain.Common;

public static class GuardClauseExtensions
{
    public static DateTime ExpiresInPast(this IGuardClause guardClause, DateTime input, string parameterName)
    {
        if (input < DateTime.UtcNow)
        {
            throw new ArgumentException($"Required input {parameterName} must not be in the past.", parameterName);
        }

        return input;
    }
}
