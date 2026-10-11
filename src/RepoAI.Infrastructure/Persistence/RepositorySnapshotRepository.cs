using Microsoft.EntityFrameworkCore;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class RepositorySnapshotRepository(RepoAIDbContext dbContext) : IRepositorySnapshotRepository
{
    public Task<RepositorySnapshot?> FindForProjectAsync(
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        dbContext.RepositorySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot => snapshot.Id == snapshotId && snapshot.ProjectId == projectId,
                cancellationToken);

    public async Task<IReadOnlyList<RepositorySnapshot>> ListForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken) =>
        await dbContext.RepositorySnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == projectId
                && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId))
            .OrderByDescending(snapshot => snapshot.ImportedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<RepositorySnapshot?> FindForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        dbContext.RepositorySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot => snapshot.Id == snapshotId
                    && snapshot.ProjectId == projectId
                    && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId),
                cancellationToken);

    public async Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(
        Guid snapshotId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken) =>
        await dbContext.RepositoryFiles
            .AsNoTracking()
            .Where(file => file.SnapshotId == snapshotId)
            .OrderBy(file => file.RelativePath)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RepositoryFile>> ListAllFilesAsync(
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        await dbContext.RepositoryFiles
            .AsNoTracking()
            .Where(file => file.SnapshotId == snapshotId)
            .OrderBy(file => file.RelativePath)
            .ToListAsync(cancellationToken);
}
