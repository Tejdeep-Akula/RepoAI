namespace RepoAI.Api.Contracts;

public sealed record BackgroundJobResponse(
    Guid Id,
    Guid ProjectId,
    string Kind,
    string Status,
    int ProgressPercentage,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode,
    string? FailureMessage);
