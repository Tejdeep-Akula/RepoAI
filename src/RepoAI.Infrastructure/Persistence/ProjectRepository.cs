using Microsoft.EntityFrameworkCore;
using RepoAI.Application.Projects;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class ProjectRepository(RepoAIDbContext dbContext) : IProjectRepository
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
}
