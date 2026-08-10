using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Users;

namespace BookStore.Core.Domain.Authentication;

public interface IUserRepository
{
    User? GetById(Guid id);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    User? GetByEmail(string email);
    User? GetByRefreshToken(string refreshToken);
    User? GetByPasswordResetToken(string hashedToken);
    void Add(User user);
    void Update(User user);
    void Delete(User user);
    bool EmailExists(string email);
    Task<List<(Book Book, DateTime AddedAt)>> GetLibraryBooksAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> IsBookInLibraryAsync(Guid userId, Guid bookId, CancellationToken cancellationToken = default);
}
