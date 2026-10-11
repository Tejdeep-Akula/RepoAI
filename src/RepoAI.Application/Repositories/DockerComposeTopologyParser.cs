using YamlDotNet.RepresentationModel;

namespace RepoAI.Application.Repositories;

/// <summary>Reads explicit Compose depends_on declarations without inferring runtime traffic.</summary>
public sealed class DockerComposeTopologyParser : IRepositoryServiceTopologyParser
{
    private static readonly HashSet<string> StandardFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "compose.yml", "compose.yaml", "docker-compose.yml", "docker-compose.yaml"
    };

    public bool CanParse(string repositoryPath)
    {
        var fileName = Path.GetFileName(repositoryPath);
        return StandardFileNames.Contains(fileName)
            || (fileName.StartsWith("compose.", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("docker-compose.", StringComparison.OrdinalIgnoreCase))
                && (fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<DeclaredServiceRelationship> Parse(string repositoryPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(content);
        if (!CanParse(repositoryPath)) return [];

        var yaml = new YamlStream();
        yaml.Load(new StringReader(content));
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root
            || !TryGet(root, "services", out var servicesNode)
            || servicesNode is not YamlMappingNode services)
        {
            return [];
        }

        var relationships = new List<DeclaredServiceRelationship>();
        foreach (var serviceEntry in services.Children)
        {
            if (serviceEntry.Key is not YamlScalarNode serviceName
                || string.IsNullOrWhiteSpace(serviceName.Value)
                || serviceEntry.Value is not YamlMappingNode serviceConfig
                || !TryGet(serviceConfig, "depends_on", out var dependsOn))
            {
                continue;
            }

            if (dependsOn is YamlSequenceNode dependencyList)
            {
                foreach (var dependency in dependencyList.Children.OfType<YamlScalarNode>())
                {
                    AddRelationship(relationships, repositoryPath, serviceName.Value, dependency.Value,
                        checked((int)dependency.Start.Line + 1));
                }
            }
            else if (dependsOn is YamlMappingNode dependencyMap)
            {
                foreach (var dependency in dependencyMap.Children)
                {
                    if (dependency.Key is YamlScalarNode dependencyName)
                    {
                        AddRelationship(relationships, repositoryPath, serviceName.Value, dependencyName.Value,
                            checked((int)dependency.Key.Start.Line + 1));
                    }
                }
            }
        }

        return relationships
            .DistinctBy(relationship => (relationship.SourceService, relationship.TargetService, relationship.ManifestPath, relationship.StartLine))
            .ToArray();
    }

    private static bool TryGet(YamlMappingNode mapping, string key, out YamlNode value) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out value!);

    private static void AddRelationship(
        ICollection<DeclaredServiceRelationship> relationships,
        string path,
        string? source,
        string? target,
        int line)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) return;
        relationships.Add(new DeclaredServiceRelationship(
            source,
            target,
            "compose_depends_on",
            path,
            line,
            line));
    }
}
