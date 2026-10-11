using System.Text.RegularExpressions;
using RepoAI.Domain.Entities;
using TreeSitter;

namespace RepoAI.Application.Repositories;

/// <summary>
/// Extracts selected source relationships from Tree-sitter syntax nodes for languages
/// without a dedicated semantic analyzer. Syntax parsing excludes ordinary comments;
/// relationship resolution remains conservative and pattern-based.
/// </summary>
public sealed partial class TreeSitterSourceRelationshipParser : IRepositoryServiceTopologyParser
{
    private static readonly IReadOnlyDictionary<string, string> GrammarByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".js"] = "javascript", [".jsx"] = "javascript", [".mjs"] = "javascript", [".cjs"] = "javascript",
            [".ts"] = "typescript", [".tsx"] = "tsx", [".py"] = "python", [".go"] = "go",
            [".java"] = "java", [".rs"] = "rust", [".rb"] = "ruby", [".php"] = "php",
            [".c"] = "c", [".h"] = "c", [".cc"] = "cpp", [".cpp"] = "cpp", [".cxx"] = "cpp",
            [".hpp"] = "cpp", [".hh"] = "cpp", [".scala"] = "scala", [".sc"] = "scala",
            [".hs"] = "haskell", [".jl"] = "julia", [".agda"] = "agda", [".ql"] = "ql"
        };

    private static readonly HashSet<string> CallNodeTypes = new(StringComparer.Ordinal)
    {
        "call_expression", "call", "method_invocation", "function_call_expression",
        "method_call_expression", "scoped_call_expression", "nullsafe_method_call_expression"
    };

    private static readonly HashSet<string> StringNodeTypes = new(StringComparer.Ordinal)
    {
        "string", "string_literal", "interpreted_string_literal", "raw_string_literal",
        "template_string", "string_content", "string_fragment", "string_value", "char_literal"
    };

    private static readonly HashSet<string> ImportNodeTypes = new(StringComparer.Ordinal)
    {
        "import_statement", "import_declaration", "using_directive", "use_declaration",
        "namespace_use_declaration", "import_from_statement", "package_clause"
    };

    private static readonly HashSet<string> StructuralDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "apps", "packages", "services", "modules"
    };

    public bool CanParse(string repositoryPath) =>
        GrammarByExtension.ContainsKey(Path.GetExtension(repositoryPath));

    public IReadOnlyList<DeclaredServiceRelationship> Parse(string repositoryPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(content);
        if (!GrammarByExtension.TryGetValue(Path.GetExtension(repositoryPath), out var grammar)) return [];

        using var language = new Language(grammar);
        using var parser = new Parser(language);
        using var tree = parser.Parse(content);
        if (tree is null) return [];
        var root = tree.RootNode;
        var component = GetComponentPath(repositoryPath);
        var imports = Descendants(root)
            .Where(node => ImportNodeTypes.Contains(node.Type))
            .Select(node => node.Text)
            .ToArray();
        var relationships = new List<DeclaredServiceRelationship>();

        foreach (var call in Descendants(root).Where(node => CallNodeTypes.Contains(node.Type)))
        {
            var callText = call.Text;
            if (!TryGetCallee(callText, out var callee)) continue;
            var leafName = callee.Split(['.', ':', '>', '-'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? callee;
            var line = call.StartPosition.Row + 1;
            var stringLiterals = Descendants(call)
                .Where(node => StringNodeTypes.Contains(node.Type))
                .Select(node => node.Text)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (IsHttpCall(callee, leafName))
            {
                foreach (var literal in stringLiterals)
                {
                    foreach (Match urlMatch in AbsoluteHttpUrlPattern().Matches(literal))
                    {
                        if (!Uri.TryCreate(urlMatch.Value, UriKind.Absolute, out var uri)) continue;
                        var confidence = literal.Contains('$') || literal.Contains('{')
                            ? "Medium"
                            : "High";
                        relationships.Add(Create(component, uri.Host, "http_endpoint_reference", repositoryPath,
                            line, "service", confidence));
                    }
                }
            }

            var database = GetDatabase(callee, leafName, stringLiterals, grammar);
            if (database is not null)
            {
                relationships.Add(Create(component, database, "database_provider_usage", repositoryPath,
                    line, "database", "Medium"));
            }

            var broker = GetBroker(callee, leafName, imports);
            if (broker is not null)
            {
                relationships.Add(Create(component, broker, "message_broker_usage", repositoryPath,
                    line, "broker", "Medium"));
            }
        }

        return relationships
            .DistinctBy(item => (item.SourceService, item.TargetService, item.RelationshipType, item.ManifestPath, item.StartLine))
            .ToArray();
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            yield return current;
            foreach (var child in current.NamedChildren) pending.Push(child);
        }
    }

    private static bool TryGetCallee(string callText, out string callee)
    {
        var match = CalleePattern().Match(callText);
        callee = match.Success ? match.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal) : string.Empty;
        return match.Success;
    }

    private static bool IsHttpCall(string callee, string leafName)
    {
        var method = leafName.ToLowerInvariant();
        if (method is "fetch" or "urlopen" or "getasync" or "postasync" or "putasync" or "deleteasync" or "patchasync") return true;

        var qualifier = callee[..Math.Max(0, callee.Length - leafName.Length)];
        var knownHttpClient = qualifier.Contains("axios", StringComparison.OrdinalIgnoreCase)
            || qualifier.Contains("requests", StringComparison.OrdinalIgnoreCase)
            || qualifier.Contains("httpx", StringComparison.OrdinalIgnoreCase)
            || qualifier.Contains("http.", StringComparison.OrdinalIgnoreCase)
            || qualifier.Contains("resttemplate", StringComparison.OrdinalIgnoreCase)
            || qualifier.Contains("webclient", StringComparison.OrdinalIgnoreCase)
            || qualifier.Contains("client", StringComparison.OrdinalIgnoreCase);
        return knownHttpClient && method is "get" or "post" or "put" or "delete" or "patch";
    }

    private static string? GetDatabase(string callee, string leafName, IReadOnlyList<string> literals, string grammar)
    {
        if (callee.Contains("psycopg", StringComparison.OrdinalIgnoreCase) && leafName.Equals("connect", StringComparison.OrdinalIgnoreCase)
            || callee.Contains("pgx.", StringComparison.OrdinalIgnoreCase) && leafName.StartsWith("Connect", StringComparison.OrdinalIgnoreCase)
            || literals.Any(item => item.Contains("postgres", StringComparison.OrdinalIgnoreCase)
                && leafName.Equals("Open", StringComparison.OrdinalIgnoreCase))) return "PostgreSQL";
        if (callee.Contains("pymysql", StringComparison.OrdinalIgnoreCase) || callee.Contains("MySqlConnection", StringComparison.OrdinalIgnoreCase)) return "MySQL";
        if (callee.Contains("sqlite", StringComparison.OrdinalIgnoreCase)) return "SQLite";
        if (callee.Contains("MongoClient", StringComparison.OrdinalIgnoreCase)) return "MongoDB";
        if (callee.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase)) return "SQL Server";
        if (callee.EndsWith("sql.Open", StringComparison.OrdinalIgnoreCase) || grammar == "go" && leafName == "Open")
        {
            var driver = literals.FirstOrDefault();
            if (driver is not null && driver.Contains("mysql", StringComparison.OrdinalIgnoreCase)) return "MySQL";
            if (driver is not null && driver.Contains("sqlite", StringComparison.OrdinalIgnoreCase)) return "SQLite";
            return "SQL database (driver unspecified)";
        }

        return null;
    }

    private static string? GetBroker(string callee, string leafName, IReadOnlyList<string> imports)
    {
        if (imports.Any(text => text.Contains("Azure.Messaging.ServiceBus", StringComparison.Ordinal))
            && leafName is "SendMessageAsync" or "SendMessagesAsync" or "ReceiveMessageAsync" or "ProcessMessageAsync")
            return "Azure Service Bus";
        if (imports.Any(text => text.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase))
            && leafName is "BasicPublish" or "BasicConsume" or "BasicAck") return "RabbitMQ";
        if (imports.Any(text => text.Contains("Confluent.Kafka", StringComparison.OrdinalIgnoreCase))
            && leafName is "Produce" or "ProduceAsync" or "Consume" or "Subscribe") return "Kafka";
        if (imports.Any(text => text.Contains("MassTransit", StringComparison.OrdinalIgnoreCase))
            && leafName is "Publish" or "Send" or "Consume") return "MassTransit message bus";
        if (imports.Any(text => text.Contains("celery", StringComparison.OrdinalIgnoreCase))
            && leafName is "send_task" or "apply_async") return "Celery task queue";
        if (callee.Contains("amqp", StringComparison.OrdinalIgnoreCase)
            && leafName is "publish" or "consume") return "RabbitMQ";
        return null;
    }

    private static DeclaredServiceRelationship Create(
        string component, string target, string relationshipType, string path,
        int line, string targetKind, string confidence) => new(
            component, target, relationshipType, path, line, line, "component", targetKind, confidence);

    private static string GetComponentPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1) return ".";
        if (segments.Length > 2 && StructuralDirectories.Contains(segments[0])) return $"{segments[0]}/{segments[1]}";
        return segments[0];
    }

    [GeneratedRegex("^\\s*(?:await\\s+)?(?:new\\s+)?([A-Za-z_$][\\w$]*(?:(?:\\.|::|->)\\s*[A-Za-z_$][\\w$]*)*)\\s*(?:<[^;()]*>)?\\s*\\(")]
    private static partial Regex CalleePattern();

    [GeneratedRegex("https?://[A-Za-z0-9._-]+(?::[0-9]{1,5})?(?:/[^\\s\\\"'<>]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteHttpUrlPattern();
}
