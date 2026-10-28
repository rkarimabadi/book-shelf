using BookStore.Core.Domain.Translation;
using BookStore.Core.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookStore.Infrastructure.Persistence.Configurations;

public sealed class TranslationJobConfiguration : IEntityTypeConfiguration<TranslationJob>
{
    public void Configure(EntityTypeBuilder<TranslationJob> builder)
    {
        builder.HasKey(job => job.Id);

        builder.Property(job => job.CreatedByUserId)
            .IsRequired();

        builder.Property(job => job.OriginalFileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(job => job.SourceTitle)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(job => job.Title)
            .HasMaxLength(TranslationJob.MaxTitleLength);

        builder.Property(job => job.Author)
            .HasMaxLength(TranslationJob.MaxAuthorLength);

        builder.Property(job => job.Instructions)
            .HasMaxLength(TranslationJob.MaxInstructionsLength);

        builder.Property(job => job.ModelName)
            .HasMaxLength(AiProviderSettings.MaxModelLength);

        builder.Property(job => job.ErrorMessage)
            .HasMaxLength(TranslationJob.MaxErrorLength);

        builder.Property(job => job.Mode)
            .IsRequired();

        builder.Property(job => job.Status)
            .IsRequired();

        builder.Property(job => job.CreatedAt)
            .IsRequired();

        builder.Ignore(job => job.IsActive);

        builder.HasIndex(job => job.Status);

        // Deleting the admin account removes their jobs (the work folders become orphans,
        // which is harmless; they hold nothing that is published).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(job => job.CreatedByUserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
