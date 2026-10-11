namespace RepoAI.Api.Contracts;

public sealed record RepositorySnapshotResponse(
    Guid Id,
    Guid ProjectId,
    string RepositoryUrl,
    string CommitSha,
    string ResolvedRef,
    string DefaultBranch,
    int FileCount,
    int TextFileCount,
    int BinaryFileCount,
    int SkippedFileCount,
    DateTimeOffset ImportedAtUtc);
