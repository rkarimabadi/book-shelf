using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Common;
using BookStore.Core.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly BookStoreDbContext _dbContext;

    public UserRepository(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public User? GetById(Guid id)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .Include(u => u.PasswordResetTokens)
            .Include(u => u.LibraryEntries)
            .FirstOrDefault(u => u.Id == id);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .Include(u => u.PasswordResetTokens)
            .Include(u => u.LibraryEntries)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public User? GetByEmail(string email)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .Include(u => u.PasswordResetTokens)
            .Include(u => u.LibraryEntries)
            .FirstOrDefault(u => u.Email == email.ToLowerInvariant());
    }

    public User? GetByRefreshToken(string refreshToken)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .Include(u => u.PasswordResetTokens)
            .Include(u => u.LibraryEntries)
            .FirstOrDefault(u => u.RefreshTokens.Any(rt => rt.Token == refreshToken));
    }

    public User? GetByPasswordResetToken(string hashedToken)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .Include(u => u.PasswordResetTokens)
            .Include(u => u.LibraryEntries)
            .FirstOrDefault(u => u.PasswordResetTokens.Any(token => token.Token == hashedToken));
    }

    public void Add(User user)
    {
        _dbContext.Users.Add(user);
    }

    public void Update(User user)
    {
        var autoDetect = _dbContext.ChangeTracker.AutoDetectChangesEnabled;
        _dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var trackedChildren = _dbContext.ChangeTracker.Entries<Entity>()
                .Select(entry => entry.Entity)
                .ToHashSet();

            _dbContext.Users.Update(user);

            foreach (var refreshToken in user.RefreshTokens)
            {
                if (!trackedChildren.Contains(refreshToken))
                {
                    _dbContext.Entry(refreshToken).State = EntityState.Added;
                }
            }

            foreach (var libraryEntry in user.LibraryEntries)
            {
                if (!trackedChildren.Contains(libraryEntry))
                {
                    _dbContext.Entry(libraryEntry).State = EntityState.Added;
                }
            }

            foreach (var passwordResetToken in user.PasswordResetTokens)
            {
                if (!trackedChildren.Contains(passwordResetToken))
                {
                    _dbContext.Entry(passwordResetToken).State = EntityState.Added;
                }
            }
        }
        finally
        {
            _dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    public void Delete(User user)
    {
        _dbContext.Users.Remove(user);
    }

    public bool EmailExists(string email)
    {
        return _dbContext.Users.Any(u => u.Email == email.ToLowerInvariant());
    }

    public Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<(Book Book, DateTime AddedAt)>> GetLibraryBooksAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Where(u => u.Id == userId)
            .SelectMany(u => u.LibraryEntries)
            .Join(_dbContext.Books, entry => entry.BookId, book => book.Id, (entry, book) => new { entry, book })
            .Where(joined => joined.book.IsActive) // deactivated books leave every user's library
            .OrderByDescending(joined => joined.entry.AddedAt)
            .Select(joined => new ValueTuple<Book, DateTime>(joined.book, joined.entry.AddedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsBookInLibraryAsync(Guid userId, Guid bookId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Where(u => u.Id == userId)
            .SelectMany(u => u.LibraryEntries)
            .AnyAsync(entry => entry.BookId == bookId, cancellationToken);
    }
}
