using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.ToTable("background_jobs", table =>
        {
            table.HasCheckConstraint(
                "CK_background_jobs_progress",
                "\"ProgressPercentage\" >= 0 AND \"ProgressPercentage\" <= 100");
            table.HasCheckConstraint(
                "CK_background_jobs_attempts",
                "\"AttemptCount\" >= 0 AND \"MaxAttempts\" > 0");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();

        builder.Property(job => job.ProjectId).IsRequired();
        builder.Property(job => job.Kind).IsRequired().HasMaxLength(100);
        builder.Property(job => job.PayloadJson).IsRequired().HasColumnType("jsonb");
        builder.Property(job => job.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(job => job.ProgressPercentage).IsRequired();
        builder.Property(job => job.AttemptCount).IsRequired();
        builder.Property(job => job.MaxAttempts).IsRequired();
        builder.Property(job => job.IdempotencyKey).HasMaxLength(128);
        builder.Property(job => job.LeaseOwner).HasMaxLength(200);
        builder.Property(job => job.FailureCode).HasMaxLength(100);

        builder.Property(job => job.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(job => job.AvailableAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(job => job.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(job => job.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(job => job.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(job => job.LeaseExpiresAtUtc).HasColumnType("timestamp with time zone");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(job => job.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => new { job.Status, job.AvailableAtUtc, job.CreatedAtUtc });
        builder.HasIndex(job => new { job.ProjectId, job.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
