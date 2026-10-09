using System.Text.Json;
using RepoAI.Domain.Entities;

namespace RepoAI.Application.Jobs;

public sealed class BackgroundJobService(IBackgroundJobRepository jobRepository)
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public Task<BackgroundJob> EnqueueAsync(
        Guid projectId,
        string kind,
        object payload,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new BackgroundJob
        {
            ProjectId = projectId,
            Kind = kind,
            PayloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions),
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = now,
            AvailableAtUtc = now,
            UpdatedAtUtc = now
        };

        return jobRepository.EnqueueAsync(job, cancellationToken);
    }

    public Task<BackgroundJob?> FindForOwnerAsync(
        Guid ownerId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        jobRepository.FindForOwnerAsync(ownerId, jobId, cancellationToken);

    public Task<BackgroundJob?> RequestCancellationAsync(
        Guid ownerId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        jobRepository.RequestCancellationAsync(ownerId, jobId, cancellationToken);
}
