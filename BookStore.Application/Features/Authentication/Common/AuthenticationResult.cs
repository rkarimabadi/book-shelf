namespace BookStore.Application.Features.Authentication.Common;

public record AuthenticationResult(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Token,
    string RefreshToken);