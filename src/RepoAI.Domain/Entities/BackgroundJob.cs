namespace RepoAI.Domain.Entities;

public enum BackgroundJobStatus
{
    Queued,
    Running,
    Succeeded,
    RetryWaiting,
    Failed,
    CancellationRequested,
    Cancelled
}

/// <summary>
/// Durable record for work that must outlive an API or worker process.
/// </summary>
public sealed class BackgroundJob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ProjectId { get; init; }

    /// <summary>A stable handler key such as repository.analysis.</summary>
    public required string Kind { get; init; }

    /// <summary>Versioned JSON input consumed by the matching worker handler.</summary>
    public required string PayloadJson { get; init; }

    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Queued;

    public int ProgressPercentage { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; init; } = 3;

    /// <summary>Optional per-project key that makes repeated enqueue requests idempotent.</summary>
    public string? IdempotencyKey { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Lease lets another worker recover work after a process crash.</summary>
    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public string? FailureCode { get; set; }

    /// <summary>A safe, user-visible summary of the most recent failed attempt.</summary>
    public string? FailureMessage { get; set; }
}
