using RepoAI.Domain.Entities;

namespace RepoAI.Application.Projects;

public sealed class ProjectService(IProjectRepository projectRepository)
{
    public Task<Project> CreateAsync(
        Guid ownerId,
        string name,
        string repositoryUrl,
        CancellationToken cancellationToken) =>
        projectRepository.CreateAsync(ownerId, name.Trim(), repositoryUrl, cancellationToken);

    public Task<IReadOnlyList<Project>> ListForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken) =>
        projectRepository.ListForOwnerAsync(ownerId, cancellationToken);

    public Task<Project?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken) =>
        projectRepository.FindForOwnerAsync(ownerId, projectId, cancellationToken);
}
