using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();

        builder.Property(project => project.OwnerId)
            .IsRequired();

        builder.Property(project => project.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(project => project.RepositoryUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(project => project.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(project => project.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(project => project.OwnerId);
    }
}
