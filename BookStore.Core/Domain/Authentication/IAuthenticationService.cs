using ErrorOr;
using BookStore.Core.Domain.Users;

namespace BookStore.Core.Domain.Authentication;

public interface IAuthenticationService
{
    ErrorOr<User> RegisterUser(string email, string passwordHash, string firstName, string lastName);
    ErrorOr<(User User, string RefreshToken)> LoginUser(string email, string passwordHash);
    ErrorOr<(User User, string RefreshToken)> RefreshToken(string refreshToken);
    ErrorOr<Success> LogoutUser(string refreshToken);
    ErrorOr<(User User, string ResetToken)> RequestPasswordReset(string email);
    ErrorOr<Success> ResetPassword(string email, string resetToken, string newPasswordHash);
    ErrorOr<User> GetUserByEmail(string email);
    ErrorOr<Success> DeactivateUser(string email);
    ErrorOr<Success> ActivateUser(string email);
}