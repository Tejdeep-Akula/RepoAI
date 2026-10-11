namespace RepoAI.Domain.Entities;

/// <summary>Inventory metadata for one path in a repository snapshot.</summary>
public sealed class RepositoryFile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid SnapshotId { get; init; }

    public required string RelativePath { get; init; }

    public string? DetectedLanguage { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public bool IsBinary { get; init; }

    public string? ContentStorageKey { get; init; }

    public string? SkipReason { get; init; }
}
