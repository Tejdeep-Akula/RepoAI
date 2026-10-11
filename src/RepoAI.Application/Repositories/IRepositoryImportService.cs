using RepoAI.Application.Jobs;

namespace RepoAI.Application.Repositories;

public sealed record RepositoryImportJobPayload(string RepositoryUrl, string? RequestedRef);

public interface IRepositoryImportService
{
    Task ImportAsync(
        Guid projectId,
        RepositoryImportJobPayload payload,
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken);
}
