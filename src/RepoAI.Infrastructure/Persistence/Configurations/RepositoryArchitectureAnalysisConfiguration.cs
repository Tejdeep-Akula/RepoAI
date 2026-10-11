using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryArchitectureAnalysisConfiguration
    : IEntityTypeConfiguration<RepositoryArchitectureAnalysis>
{
    public void Configure(EntityTypeBuilder<RepositoryArchitectureAnalysis> builder)
    {
        builder.ToTable("repository_architecture_reports");
        builder.HasKey(analysis => analysis.Id);
        builder.Property(analysis => analysis.Id).ValueGeneratedNever();
        builder.Property(analysis => analysis.SnapshotId).IsRequired();
        builder.Property(analysis => analysis.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(analysis => analysis.AnalyzerVersion).HasMaxLength(64).IsRequired();
        builder.Property(analysis => analysis.ReportJson).HasColumnType("jsonb").IsRequired();
        builder.Property(analysis => analysis.GeneratedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.HasOne<RepositorySnapshot>()
            .WithMany()
            .HasForeignKey(analysis => analysis.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(analysis => analysis.SnapshotId).IsUnique();
    }
}
