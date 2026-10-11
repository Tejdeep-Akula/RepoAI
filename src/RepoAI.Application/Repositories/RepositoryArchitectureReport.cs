namespace RepoAI.Application.Repositories;

public sealed record RepositoryArchitectureReport(
    string Summary,
    IReadOnlyList<RepositoryArchitectureLanguage> LanguagesAndFormats,
    IReadOnlyList<RepositoryArchitectureComponent> Components,
    IReadOnlyList<RepositoryArchitectureEvidence> LikelyEntryPoints,
    IReadOnlyList<RepositoryArchitectureEvidence> BuildAndDependencyFiles,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RepositoryArchitectureFinding> Findings,
    RepositoryArchitectureGraph? Graph = null);

public sealed record RepositoryArchitectureLanguage(string Name, int FileCount);

public sealed record RepositoryArchitectureComponent(
    string Name,
    string Path,
    int FileCount,
    IReadOnlyList<RepositoryArchitectureLanguage> LanguagesAndFormats,
    IReadOnlyList<string> SourcePaths);

public sealed record RepositoryArchitectureEvidence(string Path, string Reason);

public enum RepositoryArchitectureFindingKind
{
    Observation,
    Inference
}

public enum RepositoryArchitectureConfidence
{
    High,
    Medium,
    Low
}

public sealed record RepositoryArchitectureFinding(
    string Category,
    string Claim,
    RepositoryArchitectureFindingKind Kind,
    RepositoryArchitectureConfidence Confidence,
    string Method,
    IReadOnlyList<RepositoryArchitectureSourceReference> Evidence);

public sealed record RepositoryArchitectureSourceReference(
    string Path,
    int? StartLine = null,
    int? EndLine = null);

public sealed record RepositoryArchitectureGraph(
    IReadOnlyList<RepositoryArchitectureGraphNode> Nodes,
    IReadOnlyList<RepositoryArchitectureGraphEdge> Edges,
    bool IsTruncated,
    int OmittedEdgeCount);

public sealed record RepositoryArchitectureGraphNode(
    string Id,
    string Label,
    string Kind,
    string Detail,
    string? Path = null);

public sealed record RepositoryArchitectureGraphEdge(
    string Id,
    string SourceNodeId,
    string TargetNodeId,
    string RelationshipType,
    IReadOnlyList<RepositoryArchitectureSourceReference> Evidence,
    string Confidence = "High");

public sealed record DeclaredRepositoryDependency(
    string Name,
    string? VersionConstraint,
    string Ecosystem,
    string ManifestPath,
    int? StartLine = null,
    int? EndLine = null,
    string DependencyType = "package");

public sealed record DeclaredServiceRelationship(
    string SourceService,
    string TargetService,
    string RelationshipType,
    string ManifestPath,
    int? StartLine = null,
    int? EndLine = null,
    string SourceNodeKind = "service",
    string TargetNodeKind = "service",
    string Confidence = "High");
