using RepoAI.Domain.Entities;

namespace RepoAI.Application.Repositories;

public interface IRepositoryArchitectureAnalysisRepository
{
    Task SaveAsync(RepositoryArchitectureAnalysis analysis, CancellationToken cancellationToken);

    Task<RepositoryArchitectureAnalysis?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken);
}
