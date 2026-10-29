using BookStore.Core.Domain.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class AiProviderSettingsConfiguration : IEntityTypeConfiguration<AiProviderSettings>
{
    public void Configure(EntityTypeBuilder<AiProviderSettings> builder)
    {
        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Provider)
            .IsRequired();

        builder.Property(settings => settings.Endpoint)
            .IsRequired()
            .HasMaxLength(AiProviderSettings.MaxEndpointLength);

        builder.Property(settings => settings.Model)
            .IsRequired()
            .HasMaxLength(AiProviderSettings.MaxModelLength);

        // DataProtection output (encrypted); never the plain key.
        builder.Property(settings => settings.ProtectedApiKey)
            .HasMaxLength(4000);

        builder.Property(settings => settings.ParallelRequests)
            .IsRequired();

        builder.Property(settings => settings.ChunkWords)
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt)
            .IsRequired();

        builder.Ignore(settings => settings.HasApiKey);
    }
}
