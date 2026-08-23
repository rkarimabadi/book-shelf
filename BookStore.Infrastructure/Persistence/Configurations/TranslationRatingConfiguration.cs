using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class TranslationRatingConfiguration : IEntityTypeConfiguration<TranslationRating>
{
    public void Configure(EntityTypeBuilder<TranslationRating> builder)
    {
        builder.HasKey(rating => rating.Id);

        builder.Property(rating => rating.UserId)
            .IsRequired();

        builder.Property(rating => rating.BookId)
            .IsRequired();

        builder.Property(rating => rating.Rating)
            .IsRequired();

        builder.Property(rating => rating.UpdatedAt)
            .IsRequired();

        // One rating per user per book.
        builder.HasIndex(rating => new { rating.UserId, rating.BookId })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rating => rating.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(rating => rating.BookId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
