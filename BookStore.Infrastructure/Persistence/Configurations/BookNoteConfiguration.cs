using BookStore.Core.Domain.Books;
using BookStore.Core.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class BookNoteConfiguration : IEntityTypeConfiguration<BookNote>
{
    public void Configure(EntityTypeBuilder<BookNote> builder)
    {
        builder.HasKey(note => note.Id);

        builder.Property(note => note.UserId)
            .IsRequired();

        builder.Property(note => note.BookId)
            .IsRequired();

        builder.Property(note => note.Note)
            .IsRequired()
            .HasMaxLength(BookNote.MaxLength);

        builder.Property(note => note.UpdatedAt)
            .IsRequired();

        // One private note per user per book.
        builder.HasIndex(note => new { note.UserId, note.BookId })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(note => note.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(note => note.BookId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
