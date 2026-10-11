using RepoAI.Domain.Entities;

namespace RepoAI.Application.Jobs;

public interface IBackgroundJobRepository
{
    Task<BackgroundJob> EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken);

    Task<BackgroundJob?> FindForOwnerAsync(
        Guid ownerId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task<BackgroundJob?> RequestCancellationAsync(
        Guid ownerId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task<BackgroundJob?> ClaimNextAsync(
        string workerId,
        IReadOnlyCollection<string> supportedKinds,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> ReportProgressAndRenewLeaseAsync(
        Guid jobId,
        string workerId,
        int progressPercentage,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> IsCancellationRequestedAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken);

    Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken);

    Task CancelAsync(Guid jobId, string workerId, CancellationToken cancellationToken);

    Task FailAsync(
        Guid jobId,
        string workerId,
        string failureCode,
        string failureMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);

    Task RecoverExpiredLeasesAsync(CancellationToken cancellationToken);
}
