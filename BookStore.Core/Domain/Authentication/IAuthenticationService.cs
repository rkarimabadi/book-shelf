using ErrorOr;
using BookStore.Core.Domain.Users;

namespace BookStore.Core.Domain.Authentication;

public interface IAuthenticationService
{
    ErrorOr<User> RegisterUser(string email, string passwordHash, string firstName, string lastName, bool hasPassword = true);
    ErrorOr<(User User, string RefreshToken)> LoginUser(string email, string passwordHash);

    /// <summary>
    /// Signs in an already-verified account without a password comparison. Used by
    /// external providers (Google) whose identity claims have already been validated by
    /// the provider — the account is looked up by email and issued a fresh refresh token.
    /// </summary>
    ErrorOr<(User User, string RefreshToken)> LoginExternalUser(string email);

    ErrorOr<(User User, string RefreshToken)> RefreshToken(string refreshToken);
    ErrorOr<Success> LogoutUser(string refreshToken);
    ErrorOr<(User User, string ResetToken)> RequestPasswordReset(string email);
    ErrorOr<Success> ResetPassword(string email, string resetToken, string newPasswordHash);
    ErrorOr<Success> ChangePassword(string email, string newPasswordHash);
    ErrorOr<User> GetUserByEmail(string email);
}