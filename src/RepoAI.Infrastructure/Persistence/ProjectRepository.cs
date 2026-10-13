using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepoAI.Application.Projects;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class ProjectRepository(
    RepoAIDbContext dbContext,
    IRepositoryContentStore contentStore,
    ILogger<ProjectRepository> logger) : IProjectRepository
{
    public async Task<Project> CreateAsync(
        Guid ownerId,
        string name,
        string repositoryUrl,
        CancellationToken cancellationToken)
    {
        var project = new Project
        {
            OwnerId = ownerId,
            Name = name,
            RepositoryUrl = repositoryUrl
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<IReadOnlyList<Project>> ListForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken) =>
        await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerId == ownerId)
            .OrderByDescending(project => project.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<Project?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken) =>
        dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken);

    public async Task<ProjectDeletionResult> DeleteForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var projects = await dbContext.Projects
            .FromSqlInterpolated($"""
                SELECT *
                FROM projects
                WHERE "Id" = {projectId} AND "OwnerId" = {ownerId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var project = projects.SingleOrDefault();
        if (project is null)
        {
            return ProjectDeletionResult.NotFound;
        }

        var jobs = await dbContext.BackgroundJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM background_jobs
                WHERE "ProjectId" = {projectId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        if (jobs.Any(job => job.Status is BackgroundJobStatus.Running or BackgroundJobStatus.CancellationRequested))
        {
            return ProjectDeletionResult.ActiveJobs;
        }

        var snapshotIds = await dbContext.RepositorySnapshots
            .Where(snapshot => snapshot.ProjectId == projectId)
            .Select(snapshot => snapshot.Id)
            .ToArrayAsync(cancellationToken);

        dbContext.BackgroundJobs.RemoveRange(jobs);
        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var snapshotId in snapshotIds)
        {
            try
            {
                contentStore.DeleteSnapshot(snapshotId);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception,
                    "Could not remove stored repository content for deleted project {ProjectId}, snapshot {SnapshotId}",
                    projectId,
                    snapshotId);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception,
                    "Could not remove stored repository content for deleted project {ProjectId}, snapshot {SnapshotId}",
                    projectId,
                    snapshotId);
            }
        }

        return ProjectDeletionResult.Deleted;
    }
}
