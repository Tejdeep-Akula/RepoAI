namespace RepoAI.Domain.Entities;

/// <summary>
/// A repository workspace owned by one RepoAI user.
/// </summary>
public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // The database relationship and delete restriction will be mapped in Infrastructure.
    public required Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public required string RepositoryUrl { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
