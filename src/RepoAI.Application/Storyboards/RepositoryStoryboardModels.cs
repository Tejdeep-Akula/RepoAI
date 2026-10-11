using RepoAI.Application.Repositories;

namespace RepoAI.Application.Storyboards;

public sealed record RepositoryStoryboard(
    string Title,
    string Summary,
    string NarrationScript,
    IReadOnlyList<RepositoryStoryboardScene> Scenes);

public sealed record RepositoryStoryboardScene(
    int Order,
    string Title,
    string Purpose,
    string Narration,
    string VisualInstructions,
    int EstimatedDurationSeconds,
    IReadOnlyList<RepositoryArchitectureSourceReference> Evidence);

public sealed record StoryboardEvidenceOption(
    string Id,
    string Path,
    int? StartLine,
    int? EndLine,
    string Description);

public sealed record StoryboardGenerationInput(
    string RepositoryUrl,
    string CommitSha,
    string ResolvedRef,
    RepositoryArchitectureReport Architecture,
    IReadOnlyList<StoryboardEvidenceOption> Evidence);

public interface IRepositoryStoryboardGenerator
{
    string ModelId { get; }

    Task<RepositoryStoryboard> GenerateAsync(
        StoryboardGenerationInput input,
        CancellationToken cancellationToken);
}

public interface IRepositoryStoryboardRepository
{
    Task SaveAsync(
        Domain.Entities.RepositoryStoryboardDocument storyboard,
        CancellationToken cancellationToken);

    Task<Domain.Entities.RepositoryStoryboardDocument?> FindForSnapshotAsync(
        Guid snapshotId,
        CancellationToken cancellationToken);

    Task<Domain.Entities.RepositoryStoryboardDocument?> FindForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken);
}

public sealed record RepositoryStoryboardJobPayload(Guid SnapshotId);
