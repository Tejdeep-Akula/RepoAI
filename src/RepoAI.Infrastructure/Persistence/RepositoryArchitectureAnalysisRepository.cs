using Microsoft.EntityFrameworkCore;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class RepositoryArchitectureAnalysisRepository(RepoAIDbContext dbContext)
    : IRepositoryArchitectureAnalysisRepository
{
    public async Task SaveAsync(
        RepositoryArchitectureAnalysis analysis,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO repository_architecture_reports
                ("Id", "SnapshotId", "CommitSha", "AnalyzerVersion", "ReportJson", "GeneratedAtUtc")
            VALUES
                ({analysis.Id}, {analysis.SnapshotId}, {analysis.CommitSha}, {analysis.AnalyzerVersion},
                 CAST({analysis.ReportJson} AS jsonb), {analysis.GeneratedAtUtc})
            ON CONFLICT ("SnapshotId") DO UPDATE SET
                "CommitSha" = EXCLUDED."CommitSha",
                "AnalyzerVersion" = EXCLUDED."AnalyzerVersion",
                "ReportJson" = EXCLUDED."ReportJson",
                "GeneratedAtUtc" = EXCLUDED."GeneratedAtUtc"
            """, cancellationToken);
    }

    public Task<RepositoryArchitectureAnalysis?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        dbContext.RepositoryArchitectureAnalyses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                analysis => analysis.SnapshotId == snapshotId
                    && dbContext.RepositorySnapshots.Any(snapshot => snapshot.Id == snapshotId
                        && snapshot.ProjectId == projectId
                        && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId)),
                cancellationToken);

    public Task<RepositoryArchitectureAnalysis?> FindForSnapshotAsync(
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        dbContext.RepositoryArchitectureAnalyses
            .AsNoTracking()
            .SingleOrDefaultAsync(analysis => analysis.SnapshotId == snapshotId, cancellationToken);
}
