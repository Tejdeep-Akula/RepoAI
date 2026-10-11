namespace RepoAI.Domain.Entities;

/// <summary>A generated, evidence-grounded storyboard tied to one immutable repository snapshot.</summary>
public sealed class RepositoryStoryboardDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid SnapshotId { get; init; }

    public required string CommitSha { get; init; }

    public required string Model { get; init; }

    public required string StoryboardJson { get; set; }

    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
