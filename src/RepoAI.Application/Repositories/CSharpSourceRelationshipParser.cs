using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RepoAI.Domain.Entities;

namespace RepoAI.Application.Repositories;

/// <summary>
/// Extracts a limited set of relationships from C# syntax trees. This is syntax-aware,
/// but intentionally does not claim semantic or runtime resolution.
/// </summary>
public sealed class CSharpSourceRelationshipParser : IRepositoryServiceTopologyParser
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetAsync", "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync",
        "Get", "Post", "Put", "Delete", "Patch"
    };

    private static readonly Dictionary<string, string> DatabaseMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UseNpgsql"] = "PostgreSQL",
        ["UseSqlServer"] = "SQL Server",
        ["UseSqlite"] = "SQLite",
        ["UseMySql"] = "MySQL",
        ["UseMongoDB"] = "MongoDB"
    };

    public bool CanParse(string repositoryPath) =>
        Path.GetExtension(repositoryPath).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<DeclaredServiceRelationship> Parse(string repositoryPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(content);
        if (!CanParse(repositoryPath)) return [];

        var tree = CSharpSyntaxTree.ParseText(content);
        var root = tree.GetRoot();
        var component = GetComponentPath(repositoryPath);
        var relationships = new List<DeclaredServiceRelationship>();
        var constantUrls = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Initializer?.Value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(variable => (Name: variable.Identifier.ValueText,
                Value: ((LiteralExpressionSyntax)variable.Initializer!.Value).Token.ValueText))
            .Where(item => Uri.TryCreate(item.Value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        var brokerImports = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(usingDirective => usingDirective.Name?.ToString() ?? string.Empty)
            .ToArray();
        var broker = brokerImports.Any(name => name.StartsWith("Azure.Messaging.ServiceBus", StringComparison.Ordinal))
            ? "Azure Service Bus"
            : brokerImports.Any(name => name.StartsWith("RabbitMQ.Client", StringComparison.Ordinal))
                ? "RabbitMQ"
                : brokerImports.Any(name => name.StartsWith("Confluent.Kafka", StringComparison.Ordinal))
                    ? "Kafka"
                    : brokerImports.Any(name => name.StartsWith("MassTransit", StringComparison.Ordinal))
                        ? "MassTransit message bus"
                        : null;

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetInvokedName(invocation.Expression);
            var line = tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
            if (HttpMethods.Contains(methodName))
            {
                var directUrls = invocation.ArgumentList.Arguments
                    .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(literal => (Value: literal.Token.ValueText, Confidence: "High"));
                var variableUrls = invocation.ArgumentList.Arguments
                    .Where(argument => argument.Expression is IdentifierNameSyntax)
                    .Select(argument => ((IdentifierNameSyntax)argument.Expression).Identifier.ValueText)
                    .Where(constantUrls.ContainsKey)
                    .Select(name => (Value: constantUrls[name], Confidence: "Medium"));

                foreach (var url in directUrls.Concat(variableUrls).DistinctBy(item => item.Value))
                {
                    if (!Uri.TryCreate(url.Value, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) continue;
                    relationships.Add(CreateRelationship(component, uri.Host, "http_endpoint_reference",
                        repositoryPath, line, "service", url.Confidence));
                }
            }

            if (DatabaseMethods.TryGetValue(methodName, out var database))
            {
                relationships.Add(CreateRelationship(component, database, "database_provider_usage",
                    repositoryPath, line, "database", "Medium"));
            }

            if (broker is not null && IsBrokerOperation(methodName, broker))
            {
                relationships.Add(CreateRelationship(component, broker, "message_broker_usage",
                    repositoryPath, line, "broker", "Medium"));
            }
        }

        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = creation.Type.ToString();
            var provider = typeName switch
            {
                "NpgsqlConnection" or "NpgsqlDataSource" => "PostgreSQL",
                "SqlConnection" => "SQL Server",
                "SqliteConnection" => "SQLite",
                "MySqlConnection" or "MySqlDataSource" => "MySQL",
                "MongoClient" => "MongoDB",
                _ => null
            };
            if (provider is null) continue;
            var line = tree.GetLineSpan(creation.Span).StartLinePosition.Line + 1;
            relationships.Add(CreateRelationship(component, provider, "database_provider_usage",
                repositoryPath, line, "database", "Medium"));
        }

        return relationships
            .DistinctBy(item => (item.SourceService, item.TargetService, item.RelationshipType, item.ManifestPath, item.StartLine))
            .ToArray();
    }

    private static string GetInvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => string.Empty
    };

    private static bool IsBrokerOperation(string methodName, string broker) => broker switch
    {
        "Azure Service Bus" => methodName is "SendMessageAsync" or "SendMessagesAsync" or "ReceiveMessageAsync" or "ProcessMessageAsync",
        "RabbitMQ" => methodName is "BasicPublish" or "BasicConsume" or "BasicAck",
        "Kafka" => methodName is "Produce" or "ProduceAsync" or "Consume" or "Subscribe",
        "MassTransit message bus" => methodName is "Publish" or "Publish<T>" or "Send" or "Send<T>" or "Consume",
        _ => false
    };

    private static DeclaredServiceRelationship CreateRelationship(
        string component,
        string target,
        string relationshipType,
        string path,
        int line,
        string targetKind,
        string confidence) => new(
            component, target, relationshipType, path, line, line, "component", targetKind, confidence);

    private static string GetComponentPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1) return ".";
        if (segments.Length > 2 && segments[0] is "src" or "apps" or "packages" or "services" or "modules")
            return $"{segments[0]}/{segments[1]}";
        return segments[0];
    }
}
