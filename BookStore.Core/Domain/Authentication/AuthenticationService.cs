using System.Security.Cryptography;
using ErrorOr;
using BookStore.Core.Domain.Users;

namespace BookStore.Core.Domain.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;

    public AuthenticationService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public ErrorOr<User> RegisterUser(string email, string passwordHash, string firstName, string lastName)
    {
        var existingUser = _userRepository.GetByEmail(email);
        if (existingUser != null)
        {
            return UserErrors.Validation.EmailAlreadyExists(email);
        }

        var userResult = User.Create(email, passwordHash, firstName, lastName);
        if (userResult.IsError)
        {
            return userResult.Errors;
        }

        var user = userResult.Value;
        _userRepository.Add(user);

        return user;
    }

    public ErrorOr<(User User, string RefreshToken)> LoginUser(string email, string passwordHash)
    {
        var user = _userRepository.GetByEmail(email);
        if (user == null)
        {
            return UserErrors.Validation.InvalidCredentials;
        }

        if (user.PasswordHash != passwordHash)
        {
            return UserErrors.Validation.InvalidCredentials;
        }

        if (!user.IsActive)
        {
            return UserErrors.Validation.UserInactive(email);
        }

        var loginResult = user.Login();
        if (loginResult.IsError)
        {
            return loginResult.Errors;
        }

        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(7);

        var addRefreshTokenResult = user.AddRefreshToken(refreshToken, expiresAt);
        if (addRefreshTokenResult.IsError)
        {
            return addRefreshTokenResult.Errors;
        }

        _userRepository.Update(user);

        return (user, refreshToken);
    }

    public ErrorOr<(User User, string RefreshToken)> RefreshToken(string refreshToken)
    {
        var user = _userRepository.GetByRefreshToken(refreshToken);
        if (user == null)
        {
            return UserErrors.Validation.RefreshTokenNotFound;
        }

        var token = user.RefreshTokens.FirstOrDefault(t => t.Token == refreshToken);
        if (token == null)
        {
            return UserErrors.Validation.RefreshTokenNotFound;
        }

        if (token.IsExpired())
        {
            return UserErrors.Validation.RefreshTokenExpired;
        }

        if (token.IsRevoked)
        {
            return UserErrors.Validation.RefreshTokenRevoked;
        }

        var newRefreshToken = GenerateRefreshToken();
        var newExpiresAt = DateTime.UtcNow.AddDays(7);

        var revokeResult = user.RevokeRefreshToken(refreshToken);
        if (revokeResult.IsError)
        {
            return revokeResult.Errors;
        }

        var addResult = user.AddRefreshToken(newRefreshToken, newExpiresAt);
        if (addResult.IsError)
        {
            return addResult.Errors;
        }

        _userRepository.Update(user);

        return (user, newRefreshToken);
    }

    public ErrorOr<Success> LogoutUser(string refreshToken)
    {
        var user = _userRepository.GetByRefreshToken(refreshToken);
        if (user == null)
        {
            return UserErrors.Validation.RefreshTokenNotFound;
        }

        var revokeResult = user.RevokeRefreshToken(refreshToken);
        if (revokeResult.IsError)
        {
            return revokeResult.Errors;
        }

        _userRepository.Update(user);

        return Result.Success;
    }

    public ErrorOr<(User User, string ResetToken)> RequestPasswordReset(string email)
    {
        var user = _userRepository.GetByEmail(email);
        if (user == null)
        {
            // The handler turns this into a silent success so the endpoint never reveals
            // whether an account with the given email exists.
            return UserErrors.Validation.UserNotFound(email);
        }

        user.InvalidatePasswordResetTokens();

        var resetToken = GeneratePasswordResetToken();
        var expiresAt = DateTime.UtcNow.AddHours(PasswordResetTokenLifetimeHours);

        var addResult = user.AddPasswordResetToken(resetToken, expiresAt);
        if (addResult.IsError)
        {
            return addResult.Errors;
        }

        _userRepository.Update(user);

        return (user, resetToken);
    }

    public ErrorOr<Success> ResetPassword(string email, string resetToken, string newPasswordHash)
    {
        var tokenHash = PasswordResetToken.HashToken(resetToken);

        var user = _userRepository.GetByPasswordResetToken(tokenHash);
        if (user == null || !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return UserErrors.Validation.ResetTokenNotFound;
        }

        var resetResult = user.ResetPassword(tokenHash, newPasswordHash);
        if (resetResult.IsError)
        {
            return resetResult.Errors;
        }

        _userRepository.Update(user);

        return Result.Success;
    }

    public ErrorOr<User> GetUserByEmail(string email)
    {
        var user = _userRepository.GetByEmail(email);
        if (user == null)
        {
            return UserErrors.Validation.UserNotFound(email);
        }

        return user;
    }

    public ErrorOr<Success> DeactivateUser(string email)
    {
        var user = _userRepository.GetByEmail(email);
        if (user == null)
        {
            return UserErrors.Validation.UserNotFound(email);
        }

        user.Deactivate();
        _userRepository.Update(user);

        return Result.Success;
    }

    public ErrorOr<Success> ActivateUser(string email)
    {
        var user = _userRepository.GetByEmail(email);
        if (user == null)
        {
            return UserErrors.Validation.UserNotFound(email);
        }

        user.Activate();
        _userRepository.Update(user);

        return Result.Success;
    }

    private string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string GeneratePasswordResetToken()
    {
        // 32 random bytes -> 64 hex chars; high-entropy and only ever shown in the reset email.
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private const int PasswordResetTokenLifetimeHours = 1;
}