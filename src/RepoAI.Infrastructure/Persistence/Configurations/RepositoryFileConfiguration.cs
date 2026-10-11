using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryFileConfiguration : IEntityTypeConfiguration<RepositoryFile>
{
    public void Configure(EntityTypeBuilder<RepositoryFile> builder)
    {
        builder.ToTable("repository_files", table =>
        {
            table.HasCheckConstraint("CK_repository_files_size", "\"SizeBytes\" >= 0");
        });

        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.SnapshotId).IsRequired();
        builder.Property(file => file.RelativePath).HasMaxLength(4096).IsRequired();
        builder.Property(file => file.DetectedLanguage).HasMaxLength(100);
        builder.Property(file => file.SizeBytes).IsRequired();
        builder.Property(file => file.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(file => file.IsBinary).IsRequired();
        builder.Property(file => file.ContentStorageKey).HasMaxLength(512);
        builder.Property(file => file.SkipReason).HasMaxLength(100);
        builder.HasOne<RepositorySnapshot>()
            .WithMany()
            .HasForeignKey(file => file.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(file => new { file.SnapshotId, file.DetectedLanguage });
    }
}
