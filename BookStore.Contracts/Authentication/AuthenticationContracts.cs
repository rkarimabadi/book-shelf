namespace BookStore.Contracts.Authentication;

public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName);

/// <summary>Registration creates an unconfirmed account; no tokens until the mailed code is entered.</summary>
public record RegisterResponse(
    string Email,
    int ResendAfterSeconds);

public record ConfirmEmailRequest(
    string Email,
    string Code);

public record ResendEmailVerificationRequest(
    string Email);

public record ResendEmailVerificationResponse(
    int ResendAfterSeconds);

public record LoginRequest(
    string Email,
    string Password);

public record RefreshTokenRequest(
    string RefreshToken);

public record LogoutRequest(
    string RefreshToken);

public record ForgotPasswordRequest(
    string Email);

public record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword);

// CurrentPassword is nullable: Google-created accounts have no usable password and set
// one without proving a current password (the server skips verification for them).
public record ChangePasswordRequest(
    string? CurrentPassword,
    string NewPassword);

public record AuthenticationResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Token,
    string RefreshToken);
