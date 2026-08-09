using ErrorOr;

namespace BookStore.Core.Domain.Users;

public static class UserErrors
{
    public static class Validation
    {
        public static Error EmailAlreadyExists(string email) =>
            Error.Conflict("User.EmailAlreadyExists", $"User with email '{email}' already exists.");

        public static Error InvalidCredentials =>
            Error.Unauthorized("User.InvalidCredentials", "Invalid email or password.");

        public static Error UserNotFound(string email) =>
            Error.NotFound("User.NotFound", $"User with email '{email}' not found.");

        public static Error UserInactive(string email) =>
            Error.Unauthorized("User.Inactive", $"User account '{email}' is inactive.");

        public static Error RefreshTokenNotFound =>
            Error.NotFound("User.RefreshTokenNotFound", "Refresh token not found.");

        public static Error RefreshTokenExpired =>
            Error.Unauthorized("User.RefreshTokenExpired", "Refresh token has expired.");

        public static Error RefreshTokenRevoked =>
            Error.Unauthorized("User.RefreshTokenRevoked", "Refresh token has been revoked.");
    }
}