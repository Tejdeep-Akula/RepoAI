using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.GoogleSubject)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(user => user.GoogleSubject)
            .IsUnique();

        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(user => user.Email)
            .HasMaxLength(320);

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
    }
}
