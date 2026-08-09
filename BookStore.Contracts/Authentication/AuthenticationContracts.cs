namespace BookStore.Contracts.Authentication;

public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName);

public record LoginRequest(
    string Email,
    string Password);

public record RefreshTokenRequest(
    string RefreshToken);

public record LogoutRequest(
    string RefreshToken);

public record AuthenticationResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Token,
    string RefreshToken);
