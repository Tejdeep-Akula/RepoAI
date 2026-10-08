using RepoAI.Domain.Entities;

namespace RepoAI.Application.Projects;

public interface IProjectRepository
{
    Task<Project> CreateAsync(
        Guid ownerId,
        string name,
        string repositoryUrl,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Project>> ListForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken);

    Task<Project?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken);
}
