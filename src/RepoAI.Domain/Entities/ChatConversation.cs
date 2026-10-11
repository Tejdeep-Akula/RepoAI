namespace RepoAI.Domain.Entities;

/// <summary>A saved repository chat tied to one immutable repository snapshot.</summary>
public sealed class ChatConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ProjectId { get; init; }

    public required Guid SnapshotId { get; init; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
