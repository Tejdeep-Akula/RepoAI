using RepoAI.Domain.Entities;

namespace RepoAI.Application.Repositories;

/// <summary>Creates a language-neutral overview from repository metadata and parsed manifest content.</summary>
public sealed class RepositoryArchitectureAnalyzer
{
    public const string CurrentVersion = "architecture-graph-v7";

    private const int MaxComponents = 100;
    private const int MaxEvidencePathsPerComponent = 8;
    private const int MaxEvidenceItems = 40;
    private const int MaxLanguages = 12;
    private static readonly HashSet<string> StructuralDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "apps", "packages", "services", "modules"
    };

    private static readonly HashSet<string> BuildAndDependencyFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml",
        "bun.lock", "bun.lockb", "*.csproj", "*.fsproj", "*.vbproj", "*.sln", "*.slnx",
        "global.json", "nuget.config", "pom.xml", "build.gradle", "build.gradle.kts",
        "settings.gradle", "settings.gradle.kts", "requirements.txt", "pyproject.toml", "setup.py",
        "setup.cfg", "pipfile", "pipfile.lock", "go.mod", "go.sum", "cargo.toml", "cargo.lock",
        "composer.json", "composer.lock", "gemfile", "gemfile.lock", "mix.exs", "mix.lock",
        "pubspec.yaml", "package.swift", "cmakelists.txt", "makefile", "justfile", "dockerfile",
        "docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml",
        "docker-compose.*.yml", "docker-compose.*.yaml", "compose.*.yml", "compose.*.yaml", "terraform.tf",
        "*.tf", "requirements*.txt"
    };

    private static readonly HashSet<string> LikelyEntryPointFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "program.cs", "program.fs", "main.py", "app.py", "manage.py", "wsgi.py", "asgi.py",
        "main.go", "main.rs", "main.c", "main.cpp", "main.java", "application.java", "server.js",
        "server.ts", "index.js", "index.ts", "index.jsx", "index.tsx", "main.js", "main.ts",
        "main.jsx", "main.tsx", "main.dart", "lib.rs", "bin.go", "dockerfile", "compose.yaml",
        "docker-compose.yml", "docker-compose.yaml", "pom.xml", "package.json", "mix.exs"
    };

    public RepositoryArchitectureReport Analyze(
        IReadOnlyCollection<RepositoryFile> files,
        IReadOnlyCollection<DeclaredRepositoryDependency>? declaredDependencies = null,
        IReadOnlyCollection<DeclaredServiceRelationship>? declaredServiceRelationships = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var orderedFiles = files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var languages = CountLanguages(orderedFiles);
        var componentGroups = orderedFiles
            .GroupBy(GetComponentPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RepositoryArchitectureComponent(
                group.Key == "." ? "Repository root" : Path.GetFileName(group.Key),
                group.Key,
                group.Count(),
                CountLanguages(group),
                group.Select(file => file.RelativePath)
                    .Take(MaxEvidencePathsPerComponent)
                    .ToArray()))
            .OrderByDescending(component => component.FileCount)
            .ThenBy(component => component.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var components = componentGroups.Take(MaxComponents).ToArray();
        var entryPoints = orderedFiles
            .Where(file => LikelyEntryPointFileNames.Contains(Path.GetFileName(file.RelativePath)))
            .Take(MaxEvidenceItems)
            .Select(file => new RepositoryArchitectureEvidence(
                file.RelativePath,
                "Filename commonly identifies an application, executable, or deployment entry point."))
            .ToArray();
        var buildAndDependencyFiles = orderedFiles
            .Where(file => IsBuildOrDependencyFile(Path.GetFileName(file.RelativePath)))
            .Take(MaxEvidenceItems)
            .Select(file => new RepositoryArchitectureEvidence(
                file.RelativePath,
                "Recognized build, package, dependency, or deployment manifest by filename."))
            .ToArray();
        var dependencies = declaredDependencies ?? [];
        var serviceRelationships = declaredServiceRelationships ?? [];
        var graph = new RepositoryArchitectureGraphBuilder().Build(components, dependencies, serviceRelationships);
        var findings = CreateFindings(
            orderedFiles,
            components,
            entryPoints,
            buildAndDependencyFiles,
            dependencies,
            serviceRelationships);

        var componentCount = componentGroups.Length;
        var recognizedLanguageCount = orderedFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.DetectedLanguage)
                && !IsGenericFormat(file.DetectedLanguage))
            .Select(file => file.DetectedLanguage!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var limitations = new List<string>
        {
            "Components are inferred from repository folder structure; a folder is not necessarily an independently deployable service.",
            "Entry points are identified by filename. Dependency declarations are extracted only from supported manifests; declarations do not prove runtime use.",
            "Unsupported or malformed manifests, manifests without stored text, and manifests over the parser size limit may not contribute dependency declarations.",
            "Compose depends_on records declared startup ordering, not proof of network traffic or business-level service calls.",
            "C# uses Roslyn syntax trees; JavaScript/TypeScript, Python, Go, Java, Rust, Ruby, PHP, C/C++, Scala, Haskell, Julia, Agda, and QL use Tree-sitter syntax trees for selected relationships. F#, VB, Kotlin, and Swift currently use lexical fallback. Syntax parsing does not resolve symbols or runtime behavior; all approaches may miss indirect/dynamic calls or match unused code, so findings are not proof of runtime traffic, data flow, or service boundaries.",
            "No source code is executed, and the graph is not a complete runtime call graph.",
            "Language and format detection is best-effort and reflects imported file metadata."
        };
        if (componentCount > MaxComponents)
        {
            limitations.Add($"Only the {MaxComponents} largest components are shown.");
        }
        if (entryPoints.Length == MaxEvidenceItems || buildAndDependencyFiles.Length == MaxEvidenceItems)
        {
            limitations.Add($"Evidence lists are capped at {MaxEvidenceItems} paths each.");
        }
        if (componentGroups.Any(component => component.FileCount > MaxEvidencePathsPerComponent))
        {
            limitations.Add($"Each component shows at most {MaxEvidencePathsPerComponent} example source paths.");
        }
        if (graph.IsTruncated)
        {
            limitations.Add($"The architecture graph omits {graph.OmittedEdgeCount} dependency edge(s) to keep the visualization bounded.");
        }

        var summary = orderedFiles.Length == 0
            ? "This snapshot contains no inventoried files."
            : $"Inventory-based overview of {orderedFiles.Length:N0} files across {componentCount:N0} component folder(s) and {recognizedLanguageCount:N0} detected language(s). "
                + $"Found {entryPoints.Length:N0} likely entry point(s) and {buildAndDependencyFiles.Length:N0} build/dependency file(s).";

        return new RepositoryArchitectureReport(
            summary,
            languages,
            components,
            entryPoints,
            buildAndDependencyFiles,
            limitations,
            findings,
            graph);
    }

    private static IReadOnlyList<RepositoryArchitectureFinding> CreateFindings(
        IReadOnlyCollection<RepositoryFile> files,
        IReadOnlyCollection<RepositoryArchitectureComponent> components,
        IReadOnlyCollection<RepositoryArchitectureEvidence> entryPoints,
        IReadOnlyCollection<RepositoryArchitectureEvidence> buildAndDependencyFiles,
        IReadOnlyCollection<DeclaredRepositoryDependency> declaredDependencies,
        IReadOnlyCollection<DeclaredServiceRelationship> serviceRelationships)
    {
        var findings = new List<RepositoryArchitectureFinding>();
        foreach (var component in components)
        {
            findings.Add(new RepositoryArchitectureFinding(
                "component",
                $"{component.Name} is a candidate repository component based on its folder path.",
                RepositoryArchitectureFindingKind.Inference,
                RepositoryArchitectureConfidence.Medium,
                "repository_path_grouping",
                component.SourcePaths.Select(path => new RepositoryArchitectureSourceReference(path)).ToArray()));
        }

        foreach (var language in CountLanguages(files))
        {
            var sourcePaths = files
                .Where(file => string.Equals(file.DetectedLanguage, language.Name, StringComparison.OrdinalIgnoreCase))
                .Take(MaxEvidencePathsPerComponent)
                .Select(file => new RepositoryArchitectureSourceReference(file.RelativePath))
                .ToArray();
            findings.Add(new RepositoryArchitectureFinding(
                "detected_language_or_format",
                $"The importer classified {language.FileCount} file(s) as {language.Name}.",
                RepositoryArchitectureFindingKind.Observation,
                RepositoryArchitectureConfidence.High,
                "imported_file_metadata",
                sourcePaths));
        }

        foreach (var entryPoint in entryPoints)
        {
            findings.Add(new RepositoryArchitectureFinding(
                "likely_entry_point",
                $"{entryPoint.Path} may be an application or deployment entry point.",
                RepositoryArchitectureFindingKind.Inference,
                RepositoryArchitectureConfidence.Medium,
                "known_entry_point_filename",
                [new RepositoryArchitectureSourceReference(entryPoint.Path)]));
        }

        foreach (var manifest in buildAndDependencyFiles)
        {
            findings.Add(new RepositoryArchitectureFinding(
                "build_or_dependency_file",
                $"{manifest.Path} may be a build, dependency, or deployment manifest.",
                RepositoryArchitectureFindingKind.Inference,
                RepositoryArchitectureConfidence.Medium,
                "known_manifest_filename",
                [new RepositoryArchitectureSourceReference(manifest.Path)]));
        }

        foreach (var dependency in declaredDependencies)
        {
            var version = string.IsNullOrWhiteSpace(dependency.VersionConstraint)
                ? "without a version constraint"
                : $"with version constraint '{dependency.VersionConstraint}'";
            findings.Add(new RepositoryArchitectureFinding(
                "declared_dependency",
                $"{dependency.Ecosystem} manifest declares dependency '{dependency.Name}' {version}.",
                RepositoryArchitectureFindingKind.Observation,
                RepositoryArchitectureConfidence.High,
                "manifest_dependency_declaration",
                [new RepositoryArchitectureSourceReference(
                    dependency.ManifestPath,
                    dependency.StartLine,
                    dependency.EndLine)]));
        }

        foreach (var relationship in serviceRelationships)
        {
            var claim = relationship.RelationshipType switch
            {
                "compose_depends_on" => $"Docker Compose declares that service '{relationship.SourceService}' depends on '{relationship.TargetService}' for startup.",
                "http_endpoint_reference" => $"Source code in '{relationship.SourceService}' contains an HTTP-client line referencing host '{relationship.TargetService}'.",
                "database_provider_usage" => $"Source code in '{relationship.SourceService}' contains a reference to database provider '{relationship.TargetService}'.",
                "message_broker_usage" => $"Source code in '{relationship.SourceService}' contains a reference to messaging technology '{relationship.TargetService}'.",
                _ => $"Source '{relationship.SourceService}' declares or references '{relationship.TargetService}' ({relationship.RelationshipType})."
            };
            var kind = relationship.RelationshipType == "compose_depends_on"
                ? RepositoryArchitectureFindingKind.Observation
                : RepositoryArchitectureFindingKind.Inference;
            var confidence = Enum.TryParse<RepositoryArchitectureConfidence>(relationship.Confidence, true, out var parsedConfidence)
                ? parsedConfidence
                : RepositoryArchitectureConfidence.Low;
            findings.Add(new RepositoryArchitectureFinding(
                relationship.RelationshipType,
                claim,
                kind,
                confidence,
                relationship.RelationshipType,
                [new RepositoryArchitectureSourceReference(
                    relationship.ManifestPath,
                    relationship.StartLine,
                    relationship.EndLine)]));
        }

        return findings;
    }

    private static IReadOnlyList<RepositoryArchitectureLanguage> CountLanguages(IEnumerable<RepositoryFile> files) =>
        files
            .Where(file => !string.IsNullOrWhiteSpace(file.DetectedLanguage))
            .GroupBy(file => file.DetectedLanguage!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RepositoryArchitectureLanguage(group.Key, group.Count()))
            .OrderByDescending(language => language.FileCount)
            .ThenBy(language => language.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxLanguages)
            .ToArray();

    private static string GetComponentPath(RepositoryFile file)
    {
        var segments = file.RelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return ".";
        }

        if (segments.Length == 1)
        {
            return ".";
        }

        if (segments.Length > 2
            && StructuralDirectoryNames.Contains(segments[0]))
        {
            return $"{segments[0]}/{segments[1]}";
        }

        return segments[0];
    }

    private static bool IsBuildOrDependencyFile(string fileName) => BuildAndDependencyFileNames.Any(pattern =>
    {
        var wildcardIndex = pattern.IndexOf('*');
        if (wildcardIndex < 0)
        {
            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = pattern[..wildcardIndex];
        var suffix = pattern[(wildcardIndex + 1)..];
        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    });

    private static bool IsGenericFormat(string name) => name.ToUpperInvariant() is
        "JSON" or "JSONC" or "JSON5" or "XML" or "YAML" or "TOML" or "DOCUMENTATION" or "TEXT" or "CSV";
}
