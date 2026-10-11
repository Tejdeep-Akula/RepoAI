namespace RepoAI.Application.Repositories;

/// <summary>Builds a bounded, evidence-backed dependency graph from known components and manifest declarations.</summary>
public sealed class RepositoryArchitectureGraphBuilder
{
    private const int MaximumEdges = 500;
    private static readonly HashSet<string> StructuralDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "apps", "packages", "services", "modules"
    };

    public RepositoryArchitectureGraph Build(
        IReadOnlyCollection<RepositoryArchitectureComponent> components,
        IReadOnlyCollection<DeclaredRepositoryDependency> dependencies,
        IReadOnlyCollection<DeclaredServiceRelationship>? serviceRelationships = null)
    {
        var nodes = components
            .Select(component => new RepositoryArchitectureGraphNode(
                ComponentId(component.Path),
                component.Name,
                "component",
                $"{component.FileCount} files",
                component.Path))
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        var edges = new List<RepositoryArchitectureGraphEdge>();
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var serviceEdges = serviceRelationships ?? [];

        foreach (var dependency in dependencies
                     .OrderBy(item => item.ManifestPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (edges.Count >= MaximumEdges) break;
            var sourceComponentPath = GetComponentPath(dependency.ManifestPath);
            var sourceId = ComponentId(sourceComponentPath);
            if (!nodes.ContainsKey(sourceId)) continue;

            string targetId;
            string relationship;
            if (dependency.DependencyType == "project_reference")
            {
                var targetPath = ResolveRelativePath(dependency.ManifestPath, dependency.Name);
                var targetComponentPath = GetComponentPath(targetPath);
                var targetComponentId = ComponentId(targetComponentPath);
                if (nodes.ContainsKey(targetComponentId))
                {
                    targetId = targetComponentId;
                }
                else
                {
                    targetId = $"project:{targetPath}";
                    nodes.TryAdd(targetId, new RepositoryArchitectureGraphNode(
                        targetId,
                        Path.GetFileNameWithoutExtension(targetPath),
                        "project",
                        "Referenced project file",
                        targetPath));
                }

                relationship = "project_reference";
            }
            else
            {
                var packageKey = string.Join('|', dependency.Ecosystem, dependency.Name, dependency.VersionConstraint ?? "");
                targetId = $"package:{packageKey}";
                nodes.TryAdd(targetId, new RepositoryArchitectureGraphNode(
                    targetId,
                    dependency.Name,
                    "package",
                    string.IsNullOrWhiteSpace(dependency.VersionConstraint)
                        ? dependency.Ecosystem
                        : $"{dependency.Ecosystem} · {dependency.VersionConstraint}"));
                relationship = "declares_package_dependency";
            }

            var edgeId = $"{sourceId}->{targetId}:{dependency.ManifestPath}:{dependency.StartLine}";
            if (!edgeIds.Add(edgeId)) continue;
            edges.Add(new RepositoryArchitectureGraphEdge(
                edgeId,
                sourceId,
                targetId,
                relationship,
                [new RepositoryArchitectureSourceReference(
                    dependency.ManifestPath,
                    dependency.StartLine,
                    dependency.EndLine)]));
        }

        foreach (var relationship in serviceEdges
                     .OrderBy(item => item.ManifestPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.SourceService, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.TargetService, StringComparer.OrdinalIgnoreCase))
        {
            if (edges.Count >= MaximumEdges) break;
            var sourceId = NodeId(relationship.SourceNodeKind, relationship.SourceService);
            var targetId = NodeId(relationship.TargetNodeKind, relationship.TargetService);
            nodes.TryAdd(sourceId, new RepositoryArchitectureGraphNode(
                sourceId,
                relationship.SourceService,
                relationship.SourceNodeKind,
                GetNodeDetail(relationship.SourceNodeKind, relationship.RelationshipType)));
            nodes.TryAdd(targetId, new RepositoryArchitectureGraphNode(
                targetId,
                relationship.TargetService,
                relationship.TargetNodeKind,
                GetNodeDetail(relationship.TargetNodeKind, relationship.RelationshipType)));
            var edgeId = $"{sourceId}->{targetId}:{relationship.ManifestPath}:{relationship.StartLine}";
            if (!edgeIds.Add(edgeId)) continue;
            edges.Add(new RepositoryArchitectureGraphEdge(
                edgeId,
                sourceId,
                targetId,
                relationship.RelationshipType,
                [new RepositoryArchitectureSourceReference(
                    relationship.ManifestPath,
                    relationship.StartLine,
                    relationship.EndLine)],
                relationship.Confidence));
        }

        var totalUniqueEdges = dependencies
            .Select(item => string.Join('|', GetComponentPath(item.ManifestPath), item.DependencyType, item.Name,
                item.VersionConstraint ?? "", item.ManifestPath, item.StartLine?.ToString() ?? ""))
            .Concat(serviceEdges.Select(item => string.Join('|', NodeId(item.SourceNodeKind, item.SourceService), item.RelationshipType,
                NodeId(item.TargetNodeKind, item.TargetService), item.ManifestPath, item.StartLine?.ToString() ?? "")))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var omitted = Math.Max(0, totalUniqueEdges - edges.Count);
        return new RepositoryArchitectureGraph(
            nodes.Values.OrderBy(node => node.Kind, StringComparer.Ordinal).ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ToArray(),
            edges,
            omitted > 0,
            omitted);
    }

    private static string ComponentId(string path) => $"component:{path}";

    private static string NodeId(string kind, string name) => $"{kind}:{name}";

    private static string GetNodeDetail(string kind, string relationshipType) => relationshipType switch
    {
        "compose_depends_on" => "Compose service · startup dependency",
        "http_endpoint_reference" => "HTTP endpoint host referenced in source",
        "database_provider_usage" => "Database provider referenced in source",
        "message_broker_usage" => "Message broker referenced in source",
        _ => kind
    };

    private static string GetComponentPath(string repositoryPath)
    {
        var segments = repositoryPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1) return ".";
        if (segments.Length > 2 && StructuralDirectories.Contains(segments[0]))
        {
            return $"{segments[0]}/{segments[1]}";
        }

        return segments[0];
    }

    private static string ResolveRelativePath(string manifestPath, string reference)
    {
        var baseSegments = manifestPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (baseSegments.Count > 0) baseSegments.RemoveAt(baseSegments.Count - 1);
        foreach (var segment in reference.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (baseSegments.Count > 0) baseSegments.RemoveAt(baseSegments.Count - 1);
                continue;
            }

            baseSegments.Add(segment);
        }

        return string.Join('/', baseSegments);
    }
}
