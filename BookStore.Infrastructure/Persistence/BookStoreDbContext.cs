using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Users;
using BookStore.Infrastructure.Persistence.Configurations;
using BookStore.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence;

public sealed class BookStoreDbContext : DbContext
{
    private readonly PublishDomainEventsInterceptor _publishDomainEventsInterceptor;

    public BookStoreDbContext(
        DbContextOptions<BookStoreDbContext> options,
        PublishDomainEventsInterceptor publishDomainEventsInterceptor)
        : base(options)
    {
        _publishDomainEventsInterceptor = publishDomainEventsInterceptor;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookStoreDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_publishDomainEventsInterceptor);
    }
}
