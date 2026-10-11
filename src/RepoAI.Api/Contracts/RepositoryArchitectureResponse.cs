namespace RepoAI.Api.Contracts;

public sealed record RepositoryArchitectureResponse(
    Guid SnapshotId,
    string CommitSha,
    string AnalyzerVersion,
    DateTimeOffset GeneratedAtUtc,
    string Summary,
    IReadOnlyList<RepositoryArchitectureLanguageResponse> LanguagesAndFormats,
    IReadOnlyList<RepositoryArchitectureComponentResponse> Components,
    IReadOnlyList<RepositoryArchitectureEvidenceResponse> LikelyEntryPoints,
    IReadOnlyList<RepositoryArchitectureEvidenceResponse> BuildAndDependencyFiles,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RepositoryArchitectureFindingResponse> Findings,
    RepositoryArchitectureGraphResponse? Graph = null);

public sealed record RepositoryArchitectureLanguageResponse(string Name, int FileCount);

public sealed record RepositoryArchitectureComponentResponse(
    string Name,
    string Path,
    int FileCount,
    IReadOnlyList<RepositoryArchitectureLanguageResponse> LanguagesAndFormats,
    IReadOnlyList<string> SourcePaths);

public sealed record RepositoryArchitectureEvidenceResponse(string Path, string Reason);

public sealed record RepositoryArchitectureFindingResponse(
    string Category,
    string Claim,
    string Kind,
    string Confidence,
    string Method,
    IReadOnlyList<RepositoryArchitectureSourceReferenceResponse> Evidence);

public sealed record RepositoryArchitectureSourceReferenceResponse(
    string Path,
    int? StartLine,
    int? EndLine);

public sealed record RepositoryArchitectureGraphResponse(
    IReadOnlyList<RepositoryArchitectureGraphNodeResponse> Nodes,
    IReadOnlyList<RepositoryArchitectureGraphEdgeResponse> Edges,
    bool IsTruncated,
    int OmittedEdgeCount);

public sealed record RepositoryArchitectureGraphNodeResponse(
    string Id,
    string Label,
    string Kind,
    string Detail,
    string? Path);

public sealed record RepositoryArchitectureGraphEdgeResponse(
    string Id,
    string SourceNodeId,
    string TargetNodeId,
    string RelationshipType,
    IReadOnlyList<RepositoryArchitectureSourceReferenceResponse> Evidence,
    string Confidence);
