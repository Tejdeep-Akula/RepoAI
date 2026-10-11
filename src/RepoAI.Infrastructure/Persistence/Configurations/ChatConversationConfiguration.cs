using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("chat_conversations", table =>
        {
            table.HasCheckConstraint("CK_chat_conversations_title", "length(trim(\"Title\")) > 0");
        });

        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).ValueGeneratedNever();
        builder.Property(conversation => conversation.ProjectId).IsRequired();
        builder.Property(conversation => conversation.SnapshotId).IsRequired();
        builder.Property(conversation => conversation.Title).HasMaxLength(160).IsRequired();
        builder.Property(conversation => conversation.CreatedAtUtc)
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(conversation => conversation.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(conversation => conversation.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RepositorySnapshot>()
            .WithMany()
            .HasForeignKey(conversation => conversation.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(conversation => new { conversation.ProjectId, conversation.UpdatedAtUtc });
        builder.HasIndex(conversation => conversation.SnapshotId);
    }
}
