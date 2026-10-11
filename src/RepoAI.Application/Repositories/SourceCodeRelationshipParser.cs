using System.Text.RegularExpressions;
using RepoAI.Domain.Entities;

namespace RepoAI.Application.Repositories;

/// <summary>
/// Finds conservative, line-local references to HTTP endpoints, database providers, and message brokers.
/// These indicate code/configuration references, not proof that a call executes at runtime.
/// </summary>
public sealed partial class SourceCodeRelationshipParser : IRepositoryServiceTopologyParser
{
    private static readonly HashSet<string> StructuralDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "apps", "packages", "services", "modules"
    };
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx",
        ".py", ".go", ".java", ".kt", ".kts", ".scala", ".rs", ".rb", ".php",
        ".swift", ".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hh"
    };

    private static readonly (Regex Pattern, string Name)[] DatabasePatterns =
    [
        (PostgreSqlPattern(), "PostgreSQL"),
        (SqlServerPattern(), "SQL Server"),
        (SqlitePattern(), "SQLite"),
        (MongoPattern(), "MongoDB"),
        (MySqlPattern(), "MySQL"),
        (GenericSqlPattern(), "SQL database (driver unspecified)")
    ];

    private static readonly (Regex Pattern, string Name)[] BrokerPatterns =
    [
        (RabbitMqPattern(), "RabbitMQ"),
        (KafkaPattern(), "Kafka"),
        (ServiceBusPattern(), "Azure Service Bus"),
        (MassTransitPattern(), "MassTransit message bus"),
        (CeleryPattern(), "Celery task queue")
    ];

    public bool CanParse(string repositoryPath) =>
        SupportedExtensions.Contains(Path.GetExtension(repositoryPath));

    public IReadOnlyList<DeclaredServiceRelationship> Parse(string repositoryPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(content);
        if (!CanParse(repositoryPath)) return [];

        var sourceComponent = GetComponentPath(repositoryPath);
        var relationships = new List<DeclaredServiceRelationship>();
        using var reader = new StringReader(content);
        var lineNumber = 0;
        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || IsComment(line, Path.GetExtension(repositoryPath))) continue;

            if (HttpClientPattern().IsMatch(line))
            {
                foreach (Match urlMatch in AbsoluteHttpUrlPattern().Matches(line))
                {
                    if (!Uri.TryCreate(urlMatch.Value.TrimEnd('.', ',', ';', ')', ']', '"', '\''), UriKind.Absolute, out var uri))
                    {
                        continue;
                    }

                    relationships.Add(new DeclaredServiceRelationship(
                        sourceComponent,
                        uri.Host,
                        "http_endpoint_reference",
                        repositoryPath,
                        lineNumber,
                        lineNumber,
                        "component",
                        "service",
                        "High"));
                }
            }

            foreach (var (pattern, database) in DatabasePatterns)
            {
                if (pattern.IsMatch(line))
                {
                    relationships.Add(new DeclaredServiceRelationship(
                        sourceComponent,
                        database,
                        "database_provider_usage",
                        repositoryPath,
                        lineNumber,
                        lineNumber,
                        "component",
                        "database",
                        "Medium"));
                }
            }

            foreach (var (pattern, broker) in BrokerPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    relationships.Add(new DeclaredServiceRelationship(
                        sourceComponent,
                        broker,
                        "message_broker_usage",
                        repositoryPath,
                        lineNumber,
                        lineNumber,
                        "component",
                        "broker",
                        "Medium"));
                }
            }
        }

        return relationships
            .DistinctBy(relationship => (relationship.SourceService, relationship.TargetService,
                relationship.RelationshipType, relationship.ManifestPath, relationship.StartLine))
            .ToArray();
    }

    private static bool IsComment(string line, string extension) =>
        line.StartsWith("//", StringComparison.Ordinal)
        || line.StartsWith("/*", StringComparison.Ordinal)
        || line.StartsWith('*')
        || line.StartsWith('#') && extension.Equals(".py", StringComparison.OrdinalIgnoreCase);

    private static string GetComponentPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1) return ".";
        if (segments.Length > 2 && StructuralDirectories.Contains(segments[0]))
        {
            return $"{segments[0]}/{segments[1]}";
        }

        return segments[0];
    }

    [GeneratedRegex("\\b(?:HttpClient|HttpRequestMessage|BaseAddress|(?:Get|Post|Put|Delete|Patch)Async\\s*\\(|fetch\\s*\\(|axios(?:\\.\\w+)?\\s*\\(|requests?\\.(?:get|post|put|delete|patch)\\s*\\(|httpx\\.(?:get|post|put|delete|patch)\\s*\\(|RestTemplate|WebClient|http\\.(?:Get|Post)\\s*\\()", RegexOptions.IgnoreCase)]
    private static partial Regex HttpClientPattern();

    [GeneratedRegex("https?://[A-Za-z0-9._-]+(?::[0-9]{1,5})?(?:/[^\\s\"'<>]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteHttpUrlPattern();

    [GeneratedRegex("\\b(?:UseNpgsql|NpgsqlConnection|NpgsqlDataSource|psycopg(?:2)?\\.connect|pgx\\.(?:Connect|ConnectConfig)|postgres(?:ql)?://)", RegexOptions.IgnoreCase)]
    private static partial Regex PostgreSqlPattern();

    [GeneratedRegex("\\b(?:UseSqlServer|SqlConnection|Microsoft\\.Data\\.SqlClient|jdbc:sqlserver:)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlServerPattern();

    [GeneratedRegex("\\b(?:UseSqlite|SqliteConnection|sqlite3\\.connect|sqlite://|jdbc:sqlite:)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlitePattern();

    [GeneratedRegex("\\b(?:MongoClient|pymongo\\.MongoClient|mongodb(?:\\+srv)?://)", RegexOptions.IgnoreCase)]
    private static partial Regex MongoPattern();

    [GeneratedRegex("\\b(?:MySqlConnection|MySqlDataSource|pymysql\\.connect|mysql://|jdbc:mysql:)", RegexOptions.IgnoreCase)]
    private static partial Regex MySqlPattern();

    [GeneratedRegex("\\b(?:sql\\.Open|database/sql)", RegexOptions.IgnoreCase)]
    private static partial Regex GenericSqlPattern();

    [GeneratedRegex("\\b(?:BasicPublish|BasicConsume|RabbitMQ\\.Client|amqp\\.publish|amqp\\.consume)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex RabbitMqPattern();

    [GeneratedRegex("\\b(?:IProducer<|IConsumer<|KafkaProducer|KafkaConsumer|ProduceAsync|Confluent\\.Kafka)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex KafkaPattern();

    [GeneratedRegex("\\b(?:ServiceBusSender|ServiceBusProcessor|SendMessageAsync|ReceiveMessageAsync)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceBusPattern();

    [GeneratedRegex("\\b(?:IPublishEndpoint|ConsumeContext<|MassTransit|IBus)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex MassTransitPattern();

    [GeneratedRegex("\\b(?:send_task|apply_async)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex CeleryPattern();
}
