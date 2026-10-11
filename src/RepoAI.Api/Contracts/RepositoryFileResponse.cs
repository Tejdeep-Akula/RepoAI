namespace RepoAI.Api.Contracts;

public sealed record RepositoryFileResponse(
    string RelativePath,
    string? DetectedLanguage,
    long SizeBytes,
    string Sha256,
    bool IsBinary,
    bool ContentAvailable,
    string? SkipReason);
