using BookStore.Core.Domain.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(b => b.Author)
            .IsRequired()
            .HasMaxLength(100);

        // Column default so rows created before categories existed land in «متفرقه» (General).
        builder.Property(b => b.Category)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(BookCategories.General);

        builder.Property(b => b.Description);

        builder.Property(b => b.CoverImagePath);

        builder.Property(b => b.FilePath)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired();
    }
}
