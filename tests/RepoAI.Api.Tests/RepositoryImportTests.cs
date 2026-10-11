using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;
using RepoAI.Infrastructure.Persistence;
using RepoAI.Infrastructure.Repositories;
using Xunit;

namespace RepoAI.Api.Tests;

public sealed class RepositoryImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CommitSha = "0123456789abcdef0123456789abcdef01234567";

    [Theory]
    [InlineData("https://github.com/dotnet/aspnetcore", "dotnet", "aspnetcore")]
    [InlineData("https://github.com/dotnet/aspnetcore.git", "dotnet", "aspnetcore")]
    [InlineData("https://github.com/DotNet/AspNetCore/", "DotNet", "AspNetCore")]
    public void GitHubRepositoryReferenceAcceptsOnlyPublicRepositoryUrls(
        string url,
        string expectedOwner,
        string expectedName)
    {
        Assert.True(GitHubRepositoryReference.TryParse(url, out var reference));
        Assert.Equal(expectedOwner, reference?.Owner);
        Assert.Equal(expectedName, reference?.Name);
    }

    [Theory]
    [InlineData("http://github.com/org/repo")]
    [InlineData("https://github.com/org/repo/tree/main")]
    [InlineData("https://github.com.evil.test/org/repo")]
    [InlineData("https://user:secret@github.com/org/repo")]
    [InlineData("https://github.com/org/repo?token=secret")]
    [InlineData("https://github.com/org/repo#fragment")]
    [InlineData("https://github.com:8443/org/repo")]
    [InlineData("https://github.com/org/%2e%2e")]
    public void GitHubRepositoryReferenceRejectsUnsupportedOrUnsafeUrls(string url) =>
        Assert.False(GitHubRepositoryReference.TryParse(url, out _));

    [Fact]
    public void ArchivePathRemovesGitHubRootAndRejectsTraversal()
    {
        Assert.Equal("src/App.cs", RepositoryArchivePath.NormalizeGitHubEntry(
            "owner-repo-sha/src/App.cs", "owner-repo-sha"));
        Assert.Throws<BackgroundJobFailureException>(() =>
            RepositoryArchivePath.NormalizeGitHubEntry("owner-repo-sha/../../outside.txt", "owner-repo-sha"));
        Assert.Throws<BackgroundJobFailureException>(() =>
            RepositoryArchivePath.NormalizeGitHubEntry("owner-repo-sha\\outside.txt", "owner-repo-sha"));
    }

    [Fact]
    public void ArchitectureAnalyzerUsesLanguageNeutralPathsAndReturnsSourceEvidence()
    {
        var files = new[]
        {
            CreateFile("src/api/Program.cs", "C#"),
            CreateFile("src/api/RepoAI.Api.csproj", "XML"),
            CreateFile("web/src/main.tsx", "TypeScript"),
            CreateFile("web/package.json", "JSON"),
            CreateFile("README.md", "Documentation"),
            CreateFile("Dockerfile", "Dockerfile")
        };

        var report = new RepositoryArchitectureAnalyzer().Analyze(files);

        Assert.Contains("6 files", report.Summary);
        Assert.Contains(report.LanguagesAndFormats, language => language.Name == "C#");
        Assert.Contains(report.LanguagesAndFormats, language => language.Name == "TypeScript");
        Assert.Contains(report.Components, component =>
            component.Path == "src/api" && component.SourcePaths.Contains("src/api/Program.cs"));
        Assert.Contains(report.LikelyEntryPoints, item => item.Path == "src/api/Program.cs");
        Assert.Contains(report.BuildAndDependencyFiles, item => item.Path == "web/package.json");
        Assert.Contains(report.BuildAndDependencyFiles, item => item.Path == "src/api/RepoAI.Api.csproj");
        Assert.NotEmpty(report.Limitations);

        var componentFinding = Assert.Single(report.Findings, finding =>
            finding.Category == "component" && finding.Claim.Contains("api", StringComparison.Ordinal));
        Assert.Equal(RepositoryArchitectureFindingKind.Inference, componentFinding.Kind);
        Assert.Equal(RepositoryArchitectureConfidence.Medium, componentFinding.Confidence);
        Assert.Contains(componentFinding.Evidence, evidence => evidence.Path == "src/api/Program.cs");

        var languageFinding = Assert.Single(report.Findings, finding =>
            finding.Category == "detected_language_or_format"
                && finding.Claim.Contains("TypeScript", StringComparison.Ordinal));
        Assert.Equal(RepositoryArchitectureFindingKind.Observation, languageFinding.Kind);
        Assert.Equal(RepositoryArchitectureConfidence.High, languageFinding.Confidence);
        Assert.Equal("imported_file_metadata", languageFinding.Method);
        Assert.Contains(languageFinding.Evidence, evidence => evidence.Path == "web/src/main.tsx");

        var entryPointFinding = Assert.Single(report.Findings, finding =>
            finding.Category == "likely_entry_point"
                && finding.Claim.Contains("Program.cs", StringComparison.Ordinal));
        Assert.Equal(RepositoryArchitectureFindingKind.Inference, entryPointFinding.Kind);
        Assert.Equal("known_entry_point_filename", entryPointFinding.Method);
        Assert.Contains(entryPointFinding.Evidence, evidence => evidence.Path == "src/api/Program.cs");

        var manifestFinding = Assert.Single(report.Findings, finding =>
            finding.Category == "build_or_dependency_file"
                && finding.Claim.Contains("package.json", StringComparison.Ordinal));
        Assert.Equal(RepositoryArchitectureFindingKind.Inference, manifestFinding.Kind);
        Assert.Equal(RepositoryArchitectureConfidence.Medium, manifestFinding.Confidence);
        Assert.Equal("known_manifest_filename", manifestFinding.Method);
        Assert.Contains(manifestFinding.Evidence, evidence => evidence.Path == "web/package.json");
    }

    [Fact]
    public void ArchitectureGraphCreatesTypedDependencyEdgesWithManifestEvidence()
    {
        var files = new[]
        {
            CreateFile("src/api/Program.cs", "C#"),
            CreateFile("src/api/RepoAI.Api.csproj", "XML"),
            CreateFile("src/domain/RepoAI.Domain.csproj", "XML"),
            CreateFile("web/src/main.tsx", "TypeScript"),
            CreateFile("web/package.json", "JSON")
        };
        var dependencies = new[]
        {
            new DeclaredRepositoryDependency("../domain/RepoAI.Domain.csproj", null, ".NET project", "src/api/RepoAI.Api.csproj", 8, 8, "project_reference"),
            new DeclaredRepositoryDependency("react", "^19.0.0", "npm", "web/package.json")
        };

        var report = new RepositoryArchitectureAnalyzer().Analyze(files, dependencies);

        Assert.NotNull(report.Graph);
        Assert.Contains(report.Graph.Nodes, node => node.Kind == "package" && node.Label == "react");
        var packageEdge = Assert.Single(report.Graph.Edges, edge => edge.RelationshipType == "declares_package_dependency");
        Assert.Equal("component:web", packageEdge.SourceNodeId);
        Assert.Contains(packageEdge.Evidence, evidence => evidence.Path == "web/package.json");
        var projectEdge = Assert.Single(report.Graph.Edges, edge => edge.RelationshipType == "project_reference");
        Assert.Equal("component:src/api", projectEdge.SourceNodeId);
        Assert.Equal("component:src/domain", projectEdge.TargetNodeId);
        Assert.Equal(8, Assert.Single(projectEdge.Evidence).StartLine);
    }

    [Fact]
    public void DockerComposeParserExtractsListAndLongSyntaxDependsOnWithLineEvidence()
    {
        const string compose = "services:\n  api:\n    depends_on:\n      - worker\n      - db\n  worker:\n    depends_on:\n      db:\n        condition: service_healthy\n  db:\n    image: postgres\n";
        var parser = new DockerComposeTopologyParser();

        var relationships = parser.Parse("compose.yaml", compose);

        Assert.Equal(3, relationships.Count);
        Assert.Contains(relationships, relationship =>
            relationship.SourceService == "api" && relationship.TargetService == "worker");
        Assert.Contains(relationships, relationship =>
            relationship.SourceService == "api" && relationship.TargetService == "db");
        var longSyntax = Assert.Single(relationships, relationship =>
            relationship.SourceService == "worker" && relationship.TargetService == "db");
        Assert.Equal("compose.yaml", longSyntax.ManifestPath);
        Assert.Equal("compose_depends_on", longSyntax.RelationshipType);
        Assert.True(longSyntax.StartLine > 0);
    }

    [Fact]
    public void SourceCodeParserExtractsHttpDatabaseAndBrokerReferencesAcrossLanguages()
    {
        const string csharp = "using Npgsql;\nawait client.GetAsync(\"http://worker:5080/jobs\");\noptions.UseNpgsql(connectionString);\nsender.SendMessageAsync(message);\n";
        const string python = "import requests\nrequests.get('https://catalog.example/api/items')\n";
        var parser = new SourceCodeRelationshipParser();

        var relationships = parser.Parse("src/api/Client.cs", csharp)
            .Concat(parser.Parse("services/catalog/client.py", python))
            .ToArray();

        Assert.Contains(relationships, relationship =>
            relationship.SourceService == "src/api"
            && relationship.TargetService == "worker"
            && relationship.RelationshipType == "http_endpoint_reference"
            && relationship.StartLine == 2
            && relationship.Confidence == "High");
        Assert.Contains(relationships, relationship =>
            relationship.TargetService == "PostgreSQL"
            && relationship.RelationshipType == "database_provider_usage"
            && relationship.TargetNodeKind == "database");
        Assert.Contains(relationships, relationship =>
            relationship.TargetService == "Azure Service Bus"
            && relationship.RelationshipType == "message_broker_usage"
            && relationship.TargetNodeKind == "broker");
        Assert.Contains(relationships, relationship =>
            relationship.SourceService == "services/catalog"
            && relationship.TargetService == "catalog.example"
            && relationship.RelationshipType == "http_endpoint_reference");
    }

    [Fact]
    public void CSharpAstParserUsesInvocationsAndIgnoresCommentsAndUnrelatedStrings()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Npgsql;
            using Azure.Messaging.ServiceBus;
            class Client {
                const string endpoint = "http://worker:5080/jobs";
                void Run(HttpClient client, DbContextOptionsBuilder options, ServiceBusSender sender) {
                    // client.GetAsync("http://commented-out/");
                    var note = "http://not-a-call/";
                    client.GetAsync(endpoint);
                    client.GetAsync("https://literal-worker:5080/jobs");
                    options.UseNpgsql("Host=db;Database=repoai");
                    _ = new NpgsqlConnection("Host=db");
                    sender.SendMessageAsync(message);
                }
            }
            """;
        var parser = new CSharpSourceRelationshipParser();

        var relationships = parser.Parse("src/api/Client.cs", source);

        var httpRelationships = relationships.Where(item => item.RelationshipType == "http_endpoint_reference").ToArray();
        Assert.Equal(2, httpRelationships.Length);
        var variableUrl = Assert.Single(httpRelationships, item => item.Confidence == "Medium");
        Assert.Equal("worker", variableUrl.TargetService);
        Assert.Equal("src/api/Client.cs", variableUrl.ManifestPath);
        Assert.Equal(9, variableUrl.StartLine);
        var literalUrl = Assert.Single(httpRelationships, item => item.Confidence == "High");
        Assert.Equal("literal-worker", literalUrl.TargetService);
        Assert.Equal(10, literalUrl.StartLine);
        Assert.Equal(2, relationships.Count(item => item.TargetService == "PostgreSQL"));
        Assert.Contains(relationships, item => item.TargetService == "Azure Service Bus"
            && item.RelationshipType == "message_broker_usage");
    }

    [Fact]
    public void TreeSitterParserExtractsSyntaxBackedRelationshipsAcrossPythonJavaScriptAndGo()
    {
        const string python = "# requests.get('http://comment-only/')\nimport requests\nimport psycopg\nrequests.get('https://catalog.example/api')\npsycopg.connect('postgresql://db/repoai')\n";
        const string javascript = "// fetch('http://comment-only/')\nfetch('https://worker.example/jobs')\n";
        const string go = "package api\nimport \"net/http\"\nfunc load() { http.Get(\"http://worker:8080/jobs\") }\n";
        var parser = new TreeSitterSourceRelationshipParser();

        var pythonRelationships = parser.Parse("services/api/client.py", python);
        var javascriptRelationships = parser.Parse("web/src/client.js", javascript);
        var goRelationships = parser.Parse("src/worker/client.go", go);

        Assert.Contains(pythonRelationships, item => item.TargetService == "catalog.example"
            && item.RelationshipType == "http_endpoint_reference" && item.StartLine == 4);
        Assert.Contains(pythonRelationships, item => item.TargetService == "PostgreSQL"
            && item.RelationshipType == "database_provider_usage");
        Assert.DoesNotContain(pythonRelationships, item => item.TargetService == "comment-only");
        Assert.Contains(javascriptRelationships, item => item.TargetService == "worker.example"
            && item.RelationshipType == "http_endpoint_reference");
        Assert.DoesNotContain(javascriptRelationships, item => item.TargetService == "comment-only");
        Assert.Contains(goRelationships, item => item.TargetService == "worker"
            && item.RelationshipType == "http_endpoint_reference");
    }

    [Fact]
    public void ArchitectureReportLabelsSourceRelationshipsAsEvidenceBasedInferences()
    {
        var file = CreateFile("src/api/Client.cs", "C#");
        var relationship = new DeclaredServiceRelationship(
            "src/api", "worker", "http_endpoint_reference", "src/api/Client.cs", 12, 12,
            "component", "service", "High");

        var report = new RepositoryArchitectureAnalyzer().Analyze([file], [], [relationship]);

        var finding = Assert.Single(report.Findings, item => item.Category == "http_endpoint_reference");
        Assert.Equal(RepositoryArchitectureFindingKind.Inference, finding.Kind);
        Assert.Contains("contains an HTTP-client line", finding.Claim);
        Assert.Contains(report.Limitations, limitation => limitation.Contains("Syntax parsing does not resolve symbols", StringComparison.Ordinal));
        var edge = Assert.Single(report.Graph!.Edges, item => item.RelationshipType == "http_endpoint_reference");
        Assert.Equal("component:src/api", edge.SourceNodeId);
        Assert.Equal("service:worker", edge.TargetNodeId);
    }

    private static RepositoryFile CreateFile(string relativePath, string language) => new()
    {
        SnapshotId = Guid.NewGuid(),
        RelativePath = relativePath,
        DetectedLanguage = language,
        SizeBytes = 1,
        Sha256 = "abc"
    };

    [Fact]
    public async Task ImportResolvesRefToCommitAndInventoriesTextAndBinaryFiles()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RepoAIDbContext>();
        var owner = new User
        {
            GoogleSubject = $"import-test-{Guid.NewGuid():N}",
            DisplayName = "Repository import test"
        };
        var project = new Project
        {
            OwnerId = owner.Id,
            Name = "Repository import test project",
            RepositoryUrl = "https://github.com/acme/sample"
        };
        dbContext.Users.Add(owner);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), $"repoai-test-{Guid.NewGuid():N}");
        var importOptions = Options.Create(new RepositoryImportOptions { StorageRoot = storageRoot });
        IRepositoryContentStore contentStore = new LocalRepositoryContentStore(importOptions);
        var httpFactory = new FixtureHttpClientFactory(CreateArchive(
            ("acme-sample-sha/src/app.py", Encoding.UTF8.GetBytes("print('hello')\n")),
            ("acme-sample-sha/assets/logo.bin", [0, 1, 2, 255])));
        RepositorySnapshot? snapshot = null;

        try
        {
            var service = new GitHubRepositoryImportService(
                httpFactory,
                dbContext,
                contentStore,
                importOptions,
                factory.Services.GetRequiredService<ILogger<GitHubRepositoryImportService>>());
            await service.ImportAsync(
                project.Id,
                new RepositoryImportJobPayload(project.RepositoryUrl, "feature/add-demo"),
                new RecordingExecutionContext(),
                CancellationToken.None);

            snapshot = await dbContext.RepositorySnapshots.SingleAsync(candidate => candidate.ProjectId == project.Id);
            Assert.Equal(CommitSha, snapshot.CommitSha);
            Assert.Equal("feature/add-demo", snapshot.ResolvedRef);
            Assert.Equal("main", snapshot.DefaultBranch);
            Assert.Equal(2, snapshot.FileCount);
            Assert.Equal(1, snapshot.TextFileCount);
            Assert.Equal(1, snapshot.BinaryFileCount);

            var files = await dbContext.RepositoryFiles
                .Where(file => file.SnapshotId == snapshot.Id)
                .ToDictionaryAsync(file => file.RelativePath);
            Assert.Equal("Python", files["src/app.py"].DetectedLanguage);
            Assert.NotNull(files["src/app.py"].ContentStorageKey);
            Assert.True(files["assets/logo.bin"].IsBinary);
            Assert.Null(files["assets/logo.bin"].ContentStorageKey);
            await using var storedText = await contentStore.OpenReadAsync(
                files["src/app.py"].ContentStorageKey!,
                CancellationToken.None);
            using var storedTextBuffer = new MemoryStream();
            await storedText.CopyToAsync(storedTextBuffer);
            Assert.Equal("print('hello')\n", Encoding.UTF8.GetString(storedTextBuffer.ToArray()));

            Assert.Contains(httpFactory.RequestedAddresses, address =>
                address == $"https://api.github.com/repos/acme/sample/commits/feature%2Fadd-demo");
            Assert.Contains(httpFactory.RequestedAddresses, address =>
                address == $"https://codeload.github.com/acme/sample/zip/{CommitSha}");
        }
        finally
        {
            if (snapshot is not null)
            {
                contentStore.DeleteSnapshot(snapshot.Id);
                dbContext.RepositorySnapshots.Remove(snapshot);
            }

            dbContext.Projects.Remove(project);
            dbContext.Users.Remove(owner);
            await dbContext.SaveChangesAsync();
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ArchitectureReportsPersistAsVersionedJsonAndRemainOwnerScoped()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RepoAIDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositoryArchitectureAnalysisRepository>();
        var owner = new User
        {
            GoogleSubject = $"architecture-test-{Guid.NewGuid():N}",
            DisplayName = "Architecture report test"
        };
        var project = new Project
        {
            OwnerId = owner.Id,
            Name = "Architecture report test project",
            RepositoryUrl = "https://github.com/acme/architecture-sample"
        };
        var snapshot = new RepositorySnapshot
        {
            ProjectId = project.Id,
            RepositoryUrl = project.RepositoryUrl,
            CommitSha = CommitSha,
            ResolvedRef = "main",
            DefaultBranch = "main"
        };
        dbContext.Users.Add(owner);
        dbContext.Projects.Add(project);
        dbContext.RepositorySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync();

        try
        {
            var firstVersion = new RepositoryArchitectureAnalysis
            {
                SnapshotId = snapshot.Id,
                CommitSha = snapshot.CommitSha,
                AnalyzerVersion = "metadata-v1",
                ReportJson = "{\"summary\":\"first report\"}"
            };
            await repository.SaveAsync(firstVersion, CancellationToken.None);

            var savedFirst = await repository.FindForOwnerAsync(
                owner.Id,
                project.Id,
                snapshot.Id,
                CancellationToken.None);
            Assert.NotNull(savedFirst);
            Assert.Equal(firstVersion.Id, savedFirst.Id);
            Assert.Equal("first report", JsonDocument.Parse(savedFirst.ReportJson)
                .RootElement.GetProperty("summary").GetString());
            Assert.Null(await repository.FindForOwnerAsync(
                Guid.NewGuid(),
                project.Id,
                snapshot.Id,
                CancellationToken.None));

            var retryVersion = new RepositoryArchitectureAnalysis
            {
                SnapshotId = snapshot.Id,
                CommitSha = snapshot.CommitSha,
                AnalyzerVersion = "metadata-v1",
                ReportJson = "{\"summary\":\"updated report\"}"
            };
            await repository.SaveAsync(retryVersion, CancellationToken.None);

            var savedRetry = await repository.FindForOwnerAsync(
                owner.Id,
                project.Id,
                snapshot.Id,
                CancellationToken.None);
            Assert.NotNull(savedRetry);
            Assert.Equal("updated report", JsonDocument.Parse(savedRetry.ReportJson)
                .RootElement.GetProperty("summary").GetString());
            Assert.Equal(snapshot.CommitSha, savedRetry.CommitSha);
        }
        finally
        {
            dbContext.RepositorySnapshots.Remove(snapshot);
            dbContext.Projects.Remove(project);
            dbContext.Users.Remove(owner);
            await dbContext.SaveChangesAsync();
        }
    }

    private static byte[] CreateArchive(params (string Path, byte[] Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private sealed class FixtureHttpClientFactory(byte[] archiveBytes) : IHttpClientFactory
    {
        public List<string> RequestedAddresses { get; } = [];

        public HttpClient CreateClient(string name) => new(new StubGitHubHandler(this, archiveBytes));

        private sealed class StubGitHubHandler(FixtureHttpClientFactory factory, byte[] archiveBytes)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var address = request.RequestUri!.AbsoluteUri;
                factory.RequestedAddresses.Add(address);
                HttpResponseMessage response;
                if (address == "https://api.github.com/repos/acme/sample")
                {
                    response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"default_branch\":\"main\"}", Encoding.UTF8, "application/json")
                    };
                }
                else if (address == "https://api.github.com/repos/acme/sample/commits/feature%2Fadd-demo")
                {
                    response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"sha\":\"{CommitSha}\"}}", Encoding.UTF8, "application/json")
                    };
                }
                else if (address == $"https://codeload.github.com/acme/sample/zip/{CommitSha}")
                {
                    response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(archiveBytes)
                    };
                }
                else
                {
                    response = new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return Task.FromResult(response);
            }
        }
    }

    private sealed class RecordingExecutionContext : IBackgroundJobExecutionContext
    {
        public Task ReportProgressAsync(int progressPercentage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
