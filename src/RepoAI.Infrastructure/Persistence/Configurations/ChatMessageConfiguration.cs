using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages", table =>
        {
            table.HasCheckConstraint("CK_chat_messages_role", "\"Role\" IN ('user', 'assistant')");
            table.HasCheckConstraint("CK_chat_messages_content", "length(trim(\"Content\")) > 0");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.ConversationId).IsRequired();
        builder.Property(message => message.Role).HasMaxLength(16).IsRequired();
        builder.Property(message => message.Content).HasColumnType("text").IsRequired();
        builder.Property(message => message.Model).HasMaxLength(128);
        builder.Property(message => message.CitationsJson).HasColumnType("jsonb");
        builder.Property(message => message.CreatedAtUtc)
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAtUtc });
    }
}
