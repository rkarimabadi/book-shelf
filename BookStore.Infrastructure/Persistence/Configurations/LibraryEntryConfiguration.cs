using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class LibraryEntryConfiguration : IEntityTypeConfiguration<LibraryEntry>
{
    public void Configure(EntityTypeBuilder<LibraryEntry> builder)
    {
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.BookId)
            .IsRequired();

        builder.Property(entry => entry.AddedAt)
            .IsRequired();

        builder.HasIndex(entry => entry.BookId);

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(entry => entry.BookId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
