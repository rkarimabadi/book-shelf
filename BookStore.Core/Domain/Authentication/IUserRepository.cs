using BookStore.Core.Domain.Users;

namespace BookStore.Core.Domain.Authentication;

public interface IUserRepository
{
    User? GetById(Guid id);
    User? GetByEmail(string email);
    User? GetByRefreshToken(string refreshToken);
    void Add(User user);
    void Update(User user);
    void Delete(User user);
    bool EmailExists(string email);
}