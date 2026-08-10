using BookStore.Core.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.HasKey(token => token.Id);

        builder.Property(token => token.Token)
            .IsRequired()
            .HasMaxLength(64); // SHA-256 hex (64 chars)

        builder.HasIndex(token => token.Token)
            .IsUnique();

        builder.Property(token => token.ExpiresAt)
            .IsRequired();

        builder.Property(token => token.IsUsed)
            .IsRequired();

        builder.Property(token => token.UsedAt);

        builder.Property(token => token.CreatedAt)
            .IsRequired();
    }
}
