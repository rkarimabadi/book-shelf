namespace BookStore.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string email, string firstName, string lastName, string role);
}