using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryStoryboardConfiguration : IEntityTypeConfiguration<RepositoryStoryboardDocument>
{
    public void Configure(EntityTypeBuilder<RepositoryStoryboardDocument> builder)
    {
        builder.ToTable("repository_storyboards");
        builder.HasKey(storyboard => storyboard.Id);
        builder.Property(storyboard => storyboard.Id).ValueGeneratedNever();
        builder.Property(storyboard => storyboard.SnapshotId).IsRequired();
        builder.Property(storyboard => storyboard.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(storyboard => storyboard.Model).HasMaxLength(128).IsRequired();
        builder.Property(storyboard => storyboard.StoryboardJson).HasColumnType("jsonb").IsRequired();
        builder.Property(storyboard => storyboard.GeneratedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.HasOne<RepositorySnapshot>()
            .WithMany()
            .HasForeignKey(storyboard => storyboard.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(storyboard => storyboard.SnapshotId).IsUnique();
    }
}
