extern alias Worker;

using System.Text.Json;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Application.Storyboards;
using RepoAI.Domain.Entities;
using Worker::RepoAI.Worker.Jobs;
using Xunit;

namespace RepoAI.Api.Tests;

public sealed class BackgroundJobHandlerTests
{
    [Fact]
    public async Task DemoHandlerReportsIncrementalProgress()
    {
        var handler = new DemoProgressJobHandler();
        var context = new RecordingExecutionContext();
        var job = new BackgroundJob
        {
            ProjectId = Guid.NewGuid(),
            Kind = BackgroundJobKinds.DemoProgress,
            PayloadJson = "{\"stepDelayMilliseconds\":0}"
        };

        await handler.ExecuteAsync(job, context, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 10).Select(step => step * 10), context.ProgressValues);
    }

    [Fact]
    public async Task DemoHandlerCanFailOnceToExerciseRetry()
    {
        var handler = new DemoProgressJobHandler();
        var context = new RecordingExecutionContext();
        var job = new BackgroundJob
        {
            ProjectId = Guid.NewGuid(),
            Kind = BackgroundJobKinds.DemoProgress,
            PayloadJson = "{\"failFirstAttempt\":true,\"stepDelayMilliseconds\":0}",
            AttemptCount = 1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(job, context, CancellationToken.None));
        Assert.Empty(context.ProgressValues);
    }

    [Fact]
    public async Task RepositoryAnalysisHandlerPersistsVersionedReportForSnapshotCommit()
    {
        var projectId = Guid.NewGuid();
        var snapshot = new RepositorySnapshot
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            RepositoryUrl = "https://github.com/acme/sample",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            ResolvedRef = "main",
            DefaultBranch = "main"
        };
        var snapshotRepository = new FakeSnapshotRepository(snapshot,
        [
            new RepositoryFile
            {
                SnapshotId = snapshot.Id,
                RelativePath = "src/api/Program.cs",
                DetectedLanguage = "C#",
                SizeBytes = 10,
                Sha256 = "abc"
            },
            new RepositoryFile
            {
                SnapshotId = snapshot.Id,
                RelativePath = "web/package.json",
                DetectedLanguage = "JSON",
                SizeBytes = 54,
                Sha256 = "def",
                ContentStorageKey = "manifest"
            },
            new RepositoryFile
            {
                SnapshotId = snapshot.Id,
                RelativePath = "compose.yaml",
                DetectedLanguage = "YAML",
                SizeBytes = 40,
                Sha256 = "ghi",
                ContentStorageKey = "compose"
            }
        ]);
        var analysisRepository = new FakeArchitectureAnalysisRepository();
        var contentStore = new FakeContentStore();
        contentStore.Add("manifest", "{\"dependencies\":{\"react\":\"^19.0.0\"}}");
        contentStore.Add("compose", "services:\n  api:\n    depends_on:\n      - db\n  db:\n    image: postgres\n");
        var parser = new RepositoryManifestDependencyParser();
        var topologyParser = new DockerComposeTopologyParser();
        var handler = new RepositoryAnalysisJobHandler(
            snapshotRepository,
            new RepositoryDependencyExtractor(contentStore, [parser]),
            new RepositoryServiceTopologyExtractor(contentStore, [topologyParser]),
            analysisRepository,
            new RepositoryArchitectureAnalyzer(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryAnalysisJobHandler>.Instance);
        var context = new RecordingExecutionContext();
        var job = new BackgroundJob
        {
            ProjectId = projectId,
            Kind = BackgroundJobKinds.RepositoryAnalysis,
            PayloadJson = JsonSerializer.Serialize(new RepositoryAnalysisJobPayload(snapshot.Id),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        await handler.ExecuteAsync(job, context, CancellationToken.None);

        Assert.Equal([10, 55, 95], context.ProgressValues);
        Assert.NotNull(analysisRepository.SavedAnalysis);
        Assert.Equal(snapshot.Id, analysisRepository.SavedAnalysis.SnapshotId);
        Assert.Equal(snapshot.CommitSha, analysisRepository.SavedAnalysis.CommitSha);
        Assert.Equal(RepositoryArchitectureAnalyzer.CurrentVersion, analysisRepository.SavedAnalysis.AnalyzerVersion);
        var report = JsonSerializer.Deserialize<RepositoryArchitectureReport>(
            analysisRepository.SavedAnalysis.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(report);
        Assert.Contains(report.Findings, finding => finding.Category == "component");
        Assert.NotNull(report.Graph);
        Assert.Contains(report.Graph.Nodes, node => node.Kind == "package" && node.Label == "react");
        Assert.Contains(report.Graph.Nodes, node => node.Kind == "service" && node.Label == "api");
        Assert.Contains(report.Graph.Edges, edge => edge.RelationshipType == "declares_package_dependency");
        Assert.Contains(report.Graph.Edges, edge => edge.RelationshipType == "compose_depends_on");
        var dependencyFinding = Assert.Single(report.Findings, finding => finding.Category == "declared_dependency");
        Assert.Contains("react", dependencyFinding.Claim);
        Assert.Equal("manifest_dependency_declaration", dependencyFinding.Method);
        Assert.Contains(dependencyFinding.Evidence, evidence => evidence.Path == "web/package.json");
    }

    [Fact]
    public async Task StoryboardHandlerUsesSavedArchitectureAndPersistsSnapshotBoundDraft()
    {
        var projectId = Guid.NewGuid();
        var snapshot = new RepositorySnapshot
        {
            Id = Guid.NewGuid(), ProjectId = projectId,
            RepositoryUrl = "https://github.com/acme/sample",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            ResolvedRef = "main", DefaultBranch = "main"
        };
        var report = new RepositoryArchitectureReport(
            "A small API service.", [], [], [], [], [],
            [new RepositoryArchitectureFinding("entry_point", "Program configures the web application.",
                RepositoryArchitectureFindingKind.Observation, RepositoryArchitectureConfidence.High,
                "source", [new RepositoryArchitectureSourceReference("src/Program.cs", 1, 8)])]);
        var analyses = new FakeArchitectureAnalysisRepository
        {
            SavedAnalysis = new RepositoryArchitectureAnalysis
            {
                SnapshotId = snapshot.Id, CommitSha = snapshot.CommitSha,
                AnalyzerVersion = "test", ReportJson = JsonSerializer.Serialize(report,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            }
        };
        var generator = new FakeStoryboardGenerator();
        var storyboardRepository = new FakeStoryboardRepository();
        var handler = new RepositoryStoryboardJobHandler(
            new FakeSnapshotRepository(snapshot, []), analyses, storyboardRepository, generator,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryStoryboardJobHandler>.Instance);
        var context = new RecordingExecutionContext();
        var job = new BackgroundJob
        {
            ProjectId = projectId, Kind = BackgroundJobKinds.RepositoryStoryboard,
            PayloadJson = JsonSerializer.Serialize(new RepositoryStoryboardJobPayload(snapshot.Id),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        await handler.ExecuteAsync(job, context, CancellationToken.None);

        Assert.Equal([10, 45, 90], context.ProgressValues);
        Assert.Contains(generator.Input!.Evidence, item => item.Path == "src/Program.cs" && item.StartLine == 1);
        Assert.Equal(snapshot.Id, storyboardRepository.Saved!.SnapshotId);
        Assert.Equal(snapshot.CommitSha, storyboardRepository.Saved.CommitSha);
        Assert.Equal("fake-model", storyboardRepository.Saved.Model);
        Assert.Contains("src/Program.cs", storyboardRepository.Saved.StoryboardJson);
    }

    [Theory]
    [InlineData("package.json", "{\"dependencies\":{\"react\":\"^19\"}}", "react", "npm")]
    [InlineData("requirements.txt", "fastapi>=0.100\n", "fastapi", "Python")]
    [InlineData("pyproject.toml", "[project]\nname = \"sample\"\ndependencies = [\"httpx>=0.27\"]\n", "httpx", "Python")]
    [InlineData("pyproject.toml", "[project.optional-dependencies]\ndev = [\"pytest>=8\"]\n", "pytest", "Python")]
    [InlineData("go.mod", "module example.com/app\nrequire example.com/lib v1.2.3\n", "example.com/lib", "Go module")]
    [InlineData("Cargo.toml", "[dependencies]\nserde = { version = \"1.0\" }\n", "serde", "Cargo")]
    [InlineData("RepoAI.Api.csproj", "<Project><ItemGroup><PackageReference Include=\"Npgsql\" Version=\"10.0.3\" /></ItemGroup></Project>", "Npgsql", "NuGet")]
    public void ManifestParserExtractsDeclaredDependenciesAcrossEcosystems(
        string path,
        string content,
        string expectedName,
        string expectedEcosystem)
    {
        var parser = new RepositoryManifestDependencyParser();

        var dependency = Assert.Single(parser.Parse(path, content));

        Assert.Equal(expectedName, dependency.Name);
        Assert.Equal(expectedEcosystem, dependency.Ecosystem);
        Assert.Equal(path, dependency.ManifestPath);
    }

    private sealed class RecordingExecutionContext : IBackgroundJobExecutionContext
    {
        public List<int> ProgressValues { get; } = [];

        public Task ReportProgressAsync(int progressPercentage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProgressValues.Add(progressPercentage);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSnapshotRepository(
        RepositorySnapshot snapshot,
        IReadOnlyList<RepositoryFile> files) : IRepositorySnapshotRepository
    {
        public Task<RepositorySnapshot?> FindForProjectAsync(
            Guid projectId,
            Guid snapshotId,
            CancellationToken cancellationToken) =>
            Task.FromResult<RepositorySnapshot?>(snapshot.ProjectId == projectId && snapshot.Id == snapshotId
                ? snapshot
                : null);

        public Task<IReadOnlyList<RepositorySnapshot>> ListForProjectOwnerAsync(
            Guid ownerId,
            Guid projectId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RepositorySnapshot?> FindForProjectOwnerAsync(
            Guid ownerId,
            Guid projectId,
            Guid snapshotId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(
            Guid snapshotId,
            int offset,
            int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RepositoryFile>> ListAllFilesAsync(
            Guid snapshotId,
            CancellationToken cancellationToken) => Task.FromResult(files);
    }

    private sealed class FakeArchitectureAnalysisRepository : IRepositoryArchitectureAnalysisRepository
    {
        public RepositoryArchitectureAnalysis? SavedAnalysis { get; set; }

        public Task SaveAsync(RepositoryArchitectureAnalysis analysis, CancellationToken cancellationToken)
        {
            SavedAnalysis = analysis;
            return Task.CompletedTask;
        }

        public Task<RepositoryArchitectureAnalysis?> FindForOwnerAsync(
            Guid ownerId,
            Guid projectId,
            Guid snapshotId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RepositoryArchitectureAnalysis?> FindForSnapshotAsync(
            Guid snapshotId,
            CancellationToken cancellationToken) => Task.FromResult(
                SavedAnalysis?.SnapshotId == snapshotId ? SavedAnalysis : null);
    }

    private sealed class FakeStoryboardGenerator : IRepositoryStoryboardGenerator
    {
        public string ModelId => "fake-model";
        public StoryboardGenerationInput? Input { get; private set; }

        public Task<RepositoryStoryboard> GenerateAsync(StoryboardGenerationInput input, CancellationToken cancellationToken)
        {
            Input = input;
            var scene = new RepositoryStoryboardScene(1, "Entry point", "Introduce the app", "The app starts here.",
                "Animate the startup sequence.", 10, [new RepositoryArchitectureSourceReference("src/Program.cs", 1, 8)]);
            return Task.FromResult(new RepositoryStoryboard("Sample walkthrough", "An API tour.",
                scene.Narration, [scene]));
        }
    }

    private sealed class FakeStoryboardRepository : IRepositoryStoryboardRepository
    {
        public RepositoryStoryboardDocument? Saved { get; private set; }
        public Task SaveAsync(RepositoryStoryboardDocument storyboard, CancellationToken cancellationToken)
        {
            Saved = storyboard;
            return Task.CompletedTask;
        }
        public Task<RepositoryStoryboardDocument?> FindForSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken) =>
            Task.FromResult<RepositoryStoryboardDocument?>(null);
        public Task<RepositoryStoryboardDocument?> FindForOwnerAsync(Guid ownerId, Guid projectId, Guid snapshotId, CancellationToken cancellationToken) =>
            Task.FromResult<RepositoryStoryboardDocument?>(null);
    }

    private sealed class FakeContentStore : IRepositoryContentStore
    {
        private readonly Dictionary<string, byte[]> _content = new(StringComparer.Ordinal);

        public void Add(string key, string content) => _content[key] = System.Text.Encoding.UTF8.GetBytes(content);

        public Task<string> SaveTextFileAsync(
            Guid snapshotId,
            string sha256,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = new MemoryStream(_content[storageKey], writable: false);
            return Task.FromResult(stream);
        }

        public void DeleteSnapshot(Guid snapshotId) => throw new NotSupportedException();
    }
}
