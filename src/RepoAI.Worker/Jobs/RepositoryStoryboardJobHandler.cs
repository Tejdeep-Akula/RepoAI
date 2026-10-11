using System.Text.Json;
using Microsoft.Extensions.Logging;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Application.Storyboards;
using RepoAI.Domain.Entities;

namespace RepoAI.Worker.Jobs;

public sealed class RepositoryStoryboardJobHandler(
    IRepositorySnapshotRepository snapshotRepository,
    IRepositoryArchitectureAnalysisRepository analysisRepository,
    IRepositoryStoryboardRepository storyboardRepository,
    IRepositoryStoryboardGenerator generator,
    ILogger<RepositoryStoryboardJobHandler> logger) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Kind => BackgroundJobKinds.RepositoryStoryboard;

    public async Task ExecuteAsync(BackgroundJob job, IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<RepositoryStoryboardJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new BackgroundJobFailureException("invalid_storyboard_payload", "The storyboard job payload is invalid.");
        var snapshot = await snapshotRepository.FindForProjectAsync(job.ProjectId, payload.SnapshotId, cancellationToken)
            ?? throw new BackgroundJobFailureException("repository_snapshot_not_found", "The repository snapshot for this storyboard job no longer exists.");
        var analysis = await analysisRepository.FindForSnapshotAsync(snapshot.Id, cancellationToken)
            ?? throw new BackgroundJobFailureException("repository_architecture_not_found", "Run architecture analysis before generating a storyboard.");
        var report = JsonSerializer.Deserialize<RepositoryArchitectureReport>(analysis.ReportJson, JsonOptions)
            ?? throw new BackgroundJobFailureException("repository_architecture_invalid", "The saved architecture report is invalid.");

        await executionContext.ReportProgressAsync(10, cancellationToken);
        var evidence = BuildEvidence(report);
        if (evidence.Count == 0)
            throw new BackgroundJobFailureException("storyboard_evidence_unavailable", "The architecture report has no source evidence for a storyboard.");

        logger.LogInformation("Generating storyboard for snapshot {SnapshotId} at commit {CommitSha} using {Model}",
            snapshot.Id, snapshot.CommitSha, generator.ModelId);
        await executionContext.ReportProgressAsync(45, cancellationToken);
        var storyboard = await generator.GenerateAsync(new StoryboardGenerationInput(
            snapshot.RepositoryUrl, snapshot.CommitSha, snapshot.ResolvedRef, report, evidence), cancellationToken);
        await executionContext.ReportProgressAsync(90, cancellationToken);
        await storyboardRepository.SaveAsync(new RepositoryStoryboardDocument
        {
            SnapshotId = snapshot.Id,
            CommitSha = snapshot.CommitSha,
            Model = generator.ModelId,
            StoryboardJson = JsonSerializer.Serialize(storyboard, JsonOptions)
        }, cancellationToken);
        logger.LogInformation("Saved storyboard for snapshot {SnapshotId} with {SceneCount} scenes",
            snapshot.Id, storyboard.Scenes.Count);
    }

    private static IReadOnlyList<StoryboardEvidenceOption> BuildEvidence(RepositoryArchitectureReport report)
    {
        var items = new List<StoryboardEvidenceOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(RepositoryArchitectureSourceReference source, string description)
        {
            if (string.IsNullOrWhiteSpace(source.Path)) return;
            var key = $"{source.Path}:{source.StartLine}:{source.EndLine}";
            if (!seen.Add(key)) return;
            items.Add(new StoryboardEvidenceOption($"E{items.Count + 1}", source.Path,
                source.StartLine, source.EndLine, description));
        }

        foreach (var finding in report.Findings)
        foreach (var source in finding.Evidence)
            Add(source, $"{finding.Kind} · {finding.Confidence}: {finding.Claim}");
        foreach (var component in report.Components)
            foreach (var path in component.SourcePaths.Take(12))
                Add(new RepositoryArchitectureSourceReference(path), $"Component {component.Name}");
        foreach (var edge in report.Graph?.Edges ?? [])
            foreach (var source in edge.Evidence)
                Add(source, $"Graph relationship: {edge.RelationshipType}");
        foreach (var entry in report.LikelyEntryPoints)
            Add(new RepositoryArchitectureSourceReference(entry.Path), entry.Reason);
        foreach (var manifest in report.BuildAndDependencyFiles)
            Add(new RepositoryArchitectureSourceReference(manifest.Path), manifest.Reason);

        return items.Take(120).ToArray();
    }
}
