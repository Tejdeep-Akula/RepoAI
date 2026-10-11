namespace RepoAI.Domain.Entities;

/// <summary>A repository tree captured at one immutable Git commit.</summary>
public sealed class RepositorySnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ProjectId { get; init; }

    public required string RepositoryUrl { get; init; }

    public required string CommitSha { get; init; }

    public required string ResolvedRef { get; init; }

    public required string DefaultBranch { get; init; }

    public int FileCount { get; set; }

    public int TextFileCount { get; set; }

    public int BinaryFileCount { get; set; }

    public int SkippedFileCount { get; set; }

    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
