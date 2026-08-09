using BookStore.Core.Domain.Authentication;
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
            .FirstOrDefault(u => u.Id == id);
    }

    public User? GetByEmail(string email)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefault(u => u.Email == email.ToLowerInvariant());
    }

    public User? GetByRefreshToken(string refreshToken)
    {
        return _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefault(u => u.RefreshTokens.Any(rt => rt.Token == refreshToken));
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
            var trackedTokens = _dbContext.ChangeTracker.Entries<RefreshToken>()
                .Select(entry => entry.Entity)
                .ToHashSet();

            _dbContext.Users.Update(user);

            foreach (var refreshToken in user.RefreshTokens)
            {
                if (!trackedTokens.Contains(refreshToken))
                {
                    _dbContext.Entry(refreshToken).State = EntityState.Added;
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
}
