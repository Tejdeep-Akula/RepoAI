namespace RepoAI.Domain.Entities;

/// <summary>A user or assistant message in a saved repository conversation.</summary>
public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ConversationId { get; init; }

    public required string Role { get; init; }

    public required string Content { get; init; }

    public string? Model { get; init; }

    public string? CitationsJson { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
