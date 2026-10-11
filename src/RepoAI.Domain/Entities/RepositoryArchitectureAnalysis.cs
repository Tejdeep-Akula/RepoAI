namespace RepoAI.Domain.Entities;

/// <summary>A versioned static-analysis report tied to one immutable repository snapshot.</summary>
public sealed class RepositoryArchitectureAnalysis
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid SnapshotId { get; init; }

    public required string CommitSha { get; init; }

    public required string AnalyzerVersion { get; init; }

    public required string ReportJson { get; set; }

    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
