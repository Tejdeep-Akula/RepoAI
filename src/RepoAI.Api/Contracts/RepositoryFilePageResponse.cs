namespace RepoAI.Api.Contracts;

public sealed record RepositoryFilePageResponse(
    Guid SnapshotId,
    int Page,
    int PageSize,
    int TotalFiles,
    IReadOnlyList<RepositoryFileResponse> Files);
