using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class RepositorySnapshotConfiguration : IEntityTypeConfiguration<RepositorySnapshot>
{
    public void Configure(EntityTypeBuilder<RepositorySnapshot> builder)
    {
        builder.ToTable("repository_snapshots", table =>
        {
            table.HasCheckConstraint("CK_repository_snapshots_counts",
                "\"FileCount\" >= 0 AND \"TextFileCount\" >= 0 AND \"BinaryFileCount\" >= 0 AND \"SkippedFileCount\" >= 0");
        });

        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).ValueGeneratedNever();
        builder.Property(snapshot => snapshot.ProjectId).IsRequired();
        builder.Property(snapshot => snapshot.RepositoryUrl).HasMaxLength(2048).IsRequired();
        builder.Property(snapshot => snapshot.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(snapshot => snapshot.ResolvedRef).HasMaxLength(255).IsRequired();
        builder.Property(snapshot => snapshot.DefaultBranch).HasMaxLength(255).IsRequired();
        builder.Property(snapshot => snapshot.FileCount).IsRequired();
        builder.Property(snapshot => snapshot.TextFileCount).IsRequired();
        builder.Property(snapshot => snapshot.BinaryFileCount).IsRequired();
        builder.Property(snapshot => snapshot.SkippedFileCount).IsRequired();
        builder.Property(snapshot => snapshot.ImportedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(snapshot => new { snapshot.ProjectId, snapshot.ImportedAtUtc });
    }
}
