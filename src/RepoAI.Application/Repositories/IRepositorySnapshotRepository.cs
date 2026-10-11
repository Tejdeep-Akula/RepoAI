using RepoAI.Domain.Entities;

namespace RepoAI.Application.Repositories;

public interface IRepositorySnapshotRepository
{
    Task<RepositorySnapshot?> FindForProjectAsync(
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositorySnapshot>> ListForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<RepositorySnapshot?> FindForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(
        Guid snapshotId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryFile>> ListAllFilesAsync(
        Guid snapshotId,
        CancellationToken cancellationToken);
}
