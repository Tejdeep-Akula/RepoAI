using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RepoAI.Application.Repositories;

/// <summary>
/// Language-neutral parser registry for common package/project manifests. Parsers report declarations,
/// not proof that a dependency is used at runtime.
/// </summary>
public sealed partial class RepositoryManifestDependencyParser : IRepositoryManifestDependencyParser
{
    public bool CanParse(string repositoryPath)
    {
        var name = Path.GetFileName(repositoryPath);
        return name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("composer.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("requirements-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("go.mod", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DeclaredRepositoryDependency> Parse(string repositoryPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(content);
        if (!CanParse(repositoryPath))
        {
            return [];
        }

        var name = Path.GetFileName(repositoryPath);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return ParseJsonDependencies(repositoryPath, content, "npm", ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"]);
        }

        if (name.Equals("composer.json", StringComparison.OrdinalIgnoreCase))
        {
            return ParseJsonDependencies(repositoryPath, content, "Composer", ["require", "require-dev"]);
        }

        if (name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            return ParseXmlDependencies(repositoryPath, content, name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase));
        }

        if (name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("requirements-", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRequirements(repositoryPath, content);
        }

        if (name.Equals("go.mod", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGoModule(repositoryPath, content);
        }

        return ParseTomlDependencies(repositoryPath, content, name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DeclaredRepositoryDependency> ParseJsonDependencies(
        string path,
        string content,
        string ecosystem,
        IReadOnlyCollection<string> sections)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var dependencies = new List<DeclaredRepositoryDependency>();
        foreach (var section in sections)
        {
            if (!document.RootElement.TryGetProperty(section, out var items)
                || items.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var item in items.EnumerateObject())
            {
                var constraint = item.Value.ValueKind == JsonValueKind.String
                    ? item.Value.GetString()
                    : item.Value.GetRawText();
                dependencies.Add(new DeclaredRepositoryDependency(
                    item.Name,
                    constraint,
                    ecosystem,
                    path));
            }
        }

        return dependencies;
    }

    private static IReadOnlyList<DeclaredRepositoryDependency> ParseXmlDependencies(
        string path,
        string content,
        bool isMaven)
    {
        var document = XDocument.Parse(content, LoadOptions.SetLineInfo);
        var dependencies = new List<DeclaredRepositoryDependency>();
        if (isMaven)
        {
            foreach (var item in document.Descendants().Where(element => element.Name.LocalName == "dependency"))
            {
                var group = item.Elements().FirstOrDefault(element => element.Name.LocalName == "groupId")?.Value;
                var artifact = item.Elements().FirstOrDefault(element => element.Name.LocalName == "artifactId")?.Value;
                if (string.IsNullOrWhiteSpace(artifact)) continue;
                var version = item.Elements().FirstOrDefault(element => element.Name.LocalName == "version")?.Value;
                dependencies.Add(CreateXmlDependency(path, $"{group}:{artifact}", version, "Maven", item));
            }
        }
        else
        {
            foreach (var item in document.Descendants().Where(element =>
                         element.Name.LocalName is "PackageReference" or "ProjectReference"))
            {
                var packageName = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update");
                if (string.IsNullOrWhiteSpace(packageName)) continue;
                var version = (string?)item.Attribute("Version")
                    ?? item.Elements().FirstOrDefault(element => element.Name.LocalName == "Version")?.Value;
                var ecosystem = item.Name.LocalName == "ProjectReference" ? ".NET project" : "NuGet";
                dependencies.Add(CreateXmlDependency(
                    path,
                    packageName,
                    version,
                    ecosystem,
                    item,
                    item.Name.LocalName == "ProjectReference" ? "project_reference" : "package"));
            }
        }

        return dependencies;
    }

    private static DeclaredRepositoryDependency CreateXmlDependency(
        string path,
        string name,
        string? version,
        string ecosystem,
        XElement element,
        string dependencyType = "package")
    {
        var line = (element as System.Xml.IXmlLineInfo)?.LineNumber;
        return new DeclaredRepositoryDependency(name, Clean(version), ecosystem, path, line, line, dependencyType);
    }

    private static IReadOnlyList<DeclaredRepositoryDependency> ParseRequirements(string path, string content)
    {
        var dependencies = new List<DeclaredRepositoryDependency>();
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Split('#', 2)[0].Trim();
            if (line.Length == 0 || line.StartsWith('-') || line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            var match = RequirementLineRegex().Match(line);
            if (!match.Success) continue;
            dependencies.Add(new DeclaredRepositoryDependency(
                match.Groups["name"].Value,
                Clean(match.Groups["constraint"].Value),
                "Python",
                path,
                index + 1,
                index + 1));
        }

        return dependencies;
    }

    private static IReadOnlyList<DeclaredRepositoryDependency> ParseGoModule(string path, string content)
    {
        var dependencies = new List<DeclaredRepositoryDependency>();
        var inRequireBlock = false;
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Split("//", 2, StringSplitOptions.None)[0].Trim();
            if (line.StartsWith("require (", StringComparison.Ordinal)) { inRequireBlock = true; continue; }
            if (inRequireBlock && line.StartsWith(')')) { inRequireBlock = false; continue; }
            if (line.StartsWith("require ", StringComparison.Ordinal)) line = line[8..].Trim();
            else if (!inRequireBlock) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            dependencies.Add(new DeclaredRepositoryDependency(parts[0], parts[1], "Go module", path, index + 1, index + 1));
        }

        return dependencies;
    }

    private static IReadOnlyList<DeclaredRepositoryDependency> ParseTomlDependencies(
        string path,
        string content,
        bool isCargo)
    {
        var dependencies = new List<DeclaredRepositoryDependency>();
        var section = string.Empty;
        var pendingArray = new StringBuilder();
        var pendingStart = 0;
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line.Trim('[', ']').Trim();
                continue;
            }

            if (pendingArray.Length > 0)
            {
                pendingArray.Append(' ').Append(line);
                if (!line.Contains(']')) continue;
                AddTomlArrayValues(dependencies, path, section, pendingArray.ToString(), pendingStart);
                pendingArray.Clear();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim().Trim('"', '\'');
            var value = line[(separator + 1)..].Trim();
            var pythonArraySection = !isCargo && (section is "project" or "project.optional-dependencies");
            if (pythonArraySection
                && (section != "project" || key == "dependencies")
                && value.StartsWith('['))
            {
                pendingStart = index + 1;
                if (value.Contains(']')) AddTomlArrayValues(dependencies, path, section, value, pendingStart);
                else pendingArray.Append(value);
                continue;
            }

            if (!IsDependencySection(section, isCargo)) continue;
            if (!isCargo && section == "tool.poetry.dependencies" && key.Equals("python", StringComparison.OrdinalIgnoreCase)) continue;

            var constraint = ExtractTomlConstraint(value);
            dependencies.Add(new DeclaredRepositoryDependency(key, constraint, isCargo ? "Cargo" : "Python", path, index + 1, index + 1));
        }

        return dependencies;
    }

    private static void AddTomlArrayValues(
        ICollection<DeclaredRepositoryDependency> dependencies,
        string path,
        string section,
        string value,
        int line)
    {
        foreach (Match match in TomlStringRegex().Matches(value))
        {
            var declaration = match.Groups[1].Value;
            var parsed = RequirementLineRegex().Match(declaration);
            if (parsed.Success)
            {
                dependencies.Add(new DeclaredRepositoryDependency(
                    parsed.Groups["name"].Value,
                    Clean(parsed.Groups["constraint"].Value),
                    "Python",
                    path,
                    line,
                    line));
            }
        }
    }

    private static bool IsDependencySection(string section, bool isCargo) => isCargo
        ? section is "dependencies" or "dev-dependencies" or "build-dependencies"
            || section.StartsWith("target.", StringComparison.Ordinal) && section.EndsWith(".dependencies", StringComparison.Ordinal)
        : section is "project.optional-dependencies" or "tool.poetry.dependencies" or "tool.poetry.dev-dependencies"
            || section.StartsWith("tool.poetry.group.", StringComparison.Ordinal) && section.EndsWith(".dependencies", StringComparison.Ordinal);

    private static string? ExtractTomlConstraint(string value)
    {
        var version = TomlVersionRegex().Match(value);
        return version.Success ? version.Groups[1].Value : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?<constraint>\\s*(?:===|==|~=|!=|<=|>=|<|>|=).*)?$")]
    private static partial Regex RequirementLineRegex();

    [GeneratedRegex("['\"]([^'\"]+)['\"]")]
    private static partial Regex TomlStringRegex();

    [GeneratedRegex("(?:version\\s*=\\s*)['\"]([^'\"]+)['\"]")]
    private static partial Regex TomlVersionRegex();
}
