using Microsoft.EntityFrameworkCore;
using RepoAI.Application.Storyboards;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class RepositoryStoryboardRepository(RepoAIDbContext dbContext) : IRepositoryStoryboardRepository
{
    public async Task SaveAsync(RepositoryStoryboardDocument storyboard, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO repository_storyboards
                ("Id", "SnapshotId", "CommitSha", "Model", "StoryboardJson", "GeneratedAtUtc")
            VALUES
                ({storyboard.Id}, {storyboard.SnapshotId}, {storyboard.CommitSha}, {storyboard.Model},
                 CAST({storyboard.StoryboardJson} AS jsonb), {storyboard.GeneratedAtUtc})
            ON CONFLICT ("SnapshotId") DO UPDATE SET
                "CommitSha" = EXCLUDED."CommitSha",
                "Model" = EXCLUDED."Model",
                "StoryboardJson" = EXCLUDED."StoryboardJson",
                "GeneratedAtUtc" = EXCLUDED."GeneratedAtUtc"
            """, cancellationToken);
    }

    public Task<RepositoryStoryboardDocument?> FindForSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken) =>
        dbContext.RepositoryStoryboards.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SnapshotId == snapshotId, cancellationToken);

    public Task<RepositoryStoryboardDocument?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        dbContext.RepositoryStoryboards.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SnapshotId == snapshotId
                && dbContext.RepositorySnapshots.Any(snapshot => snapshot.Id == snapshotId
                    && snapshot.ProjectId == projectId
                    && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId)),
                cancellationToken);
}
