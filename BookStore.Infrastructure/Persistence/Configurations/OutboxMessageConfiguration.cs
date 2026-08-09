using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasColumnType("TEXT");

        builder.Property(m => m.OccurredOn)
            .IsRequired();

        builder.Property(m => m.ProcessedOnUtc);

        builder.Property(m => m.Error);

        builder.HasIndex(m => new { m.ProcessedOnUtc, m.OccurredOn });
    }
}
