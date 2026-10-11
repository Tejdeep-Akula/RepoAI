using System.Text.Json;
using Microsoft.Extensions.Logging;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Worker.Jobs;

public sealed class RepositoryAnalysisJobHandler(
    IRepositorySnapshotRepository snapshotRepository,
    RepositoryDependencyExtractor dependencyExtractor,
    RepositoryServiceTopologyExtractor serviceTopologyExtractor,
    IRepositoryArchitectureAnalysisRepository analysisRepository,
    RepositoryArchitectureAnalyzer analyzer,
    ILogger<RepositoryAnalysisJobHandler> logger) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => BackgroundJobKinds.RepositoryAnalysis;

    public async Task ExecuteAsync(
        BackgroundJob job,
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<RepositoryAnalysisJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new BackgroundJobFailureException(
                "invalid_analysis_payload",
                "The repository analysis job payload is invalid.");

        var snapshot = await snapshotRepository.FindForProjectAsync(
            job.ProjectId,
            payload.SnapshotId,
            cancellationToken);
        if (snapshot is null)
        {
            throw new BackgroundJobFailureException(
                "repository_snapshot_not_found",
                "The repository snapshot for this analysis job no longer exists.");
        }

        logger.LogInformation(
            "Starting architecture analysis for snapshot {SnapshotId} at commit {CommitSha}",
            snapshot.Id,
            snapshot.CommitSha);
        await executionContext.ReportProgressAsync(10, cancellationToken);
        var files = await snapshotRepository.ListAllFilesAsync(snapshot.Id, cancellationToken);
        await executionContext.ReportProgressAsync(55, cancellationToken);

        var dependencies = await dependencyExtractor.ExtractAsync(files, cancellationToken);
        var serviceRelationships = await serviceTopologyExtractor.ExtractAsync(files, cancellationToken);
        var report = analyzer.Analyze(files, dependencies, serviceRelationships);
        var analysis = new RepositoryArchitectureAnalysis
        {
            SnapshotId = snapshot.Id,
            CommitSha = snapshot.CommitSha,
            AnalyzerVersion = RepositoryArchitectureAnalyzer.CurrentVersion,
            ReportJson = JsonSerializer.Serialize(report, JsonOptions),
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };

        await executionContext.ReportProgressAsync(95, cancellationToken);
        await analysisRepository.SaveAsync(analysis, cancellationToken);
        logger.LogInformation(
            "Saved architecture analysis for snapshot {SnapshotId} with {FindingCount} findings",
            snapshot.Id,
            report.Findings.Count);
    }
}
