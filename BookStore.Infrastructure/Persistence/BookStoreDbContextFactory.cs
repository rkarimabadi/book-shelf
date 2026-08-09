using BookStore.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookStore.Infrastructure.Persistence;

public sealed class BookStoreDbContextFactory : IDesignTimeDbContextFactory<BookStoreDbContext>
{
    public BookStoreDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookStoreDbContext>();
        optionsBuilder.UseSqlite("Data Source=bookstore.db");

        return new BookStoreDbContext(optionsBuilder.Options, new PublishDomainEventsInterceptor());
    }
}
