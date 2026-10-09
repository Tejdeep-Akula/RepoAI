using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RepoAI.Application.Jobs;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class BackgroundJobRepository(RepoAIDbContext dbContext) : IBackgroundJobRepository
{
    public async Task<BackgroundJob> EnqueueAsync(
        BackgroundJob job,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(job.IdempotencyKey))
        {
            var existing = await dbContext.BackgroundJobs.SingleOrDefaultAsync(
                candidate => candidate.ProjectId == job.ProjectId
                    && candidate.IdempotencyKey == job.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        dbContext.BackgroundJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return job;
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(job.IdempotencyKey))
        {
            dbContext.Entry(job).State = EntityState.Detached;
            return await dbContext.BackgroundJobs.SingleAsync(
                candidate => candidate.ProjectId == job.ProjectId
                    && candidate.IdempotencyKey == job.IdempotencyKey,
                cancellationToken);
        }
    }

    public Task<BackgroundJob?> FindForOwnerAsync(
        Guid ownerId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        dbContext.BackgroundJobs.SingleOrDefaultAsync(
            job => job.Id == jobId
                && dbContext.Projects.Any(project => project.Id == job.ProjectId && project.OwnerId == ownerId),
            cancellationToken);

    public async Task<BackgroundJob?> RequestCancellationAsync(
        Guid ownerId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await FindForOwnerAsync(ownerId, jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (job.Status is BackgroundJobStatus.Queued or BackgroundJobStatus.RetryWaiting)
        {
            job.Status = BackgroundJobStatus.Cancelled;
            job.CompletedAtUtc = now;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
        }
        else if (job.Status == BackgroundJobStatus.Running)
        {
            job.Status = BackgroundJobStatus.CancellationRequested;
        }

        job.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<BackgroundJob?> ClaimNextAsync(
        string workerId,
        IReadOnlyCollection<string> supportedKinds,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (supportedKinds.Count == 0)
        {
            return null;
        }

        await RecoverExpiredLeasesAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var supportedJobKinds = supportedKinds.ToArray();
        var candidates = await dbContext.BackgroundJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM background_jobs
                WHERE "Status" IN ('Queued', 'RetryWaiting')
                  AND "AvailableAtUtc" <= now()
                  AND "Kind" = ANY ({supportedJobKinds})
                ORDER BY "AvailableAtUtc", "CreatedAtUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);
        var job = candidates.SingleOrDefault();
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = BackgroundJobStatus.Running;
        job.AttemptCount++;
        job.StartedAtUtc ??= now;
        job.UpdatedAtUtc = now;
        job.LeaseOwner = workerId;
        job.LeaseExpiresAtUtc = now.Add(leaseDuration);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task<bool> ReportProgressAndRenewLeaseAsync(
        Guid jobId,
        string workerId,
        int progressPercentage,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(progressPercentage, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progressPercentage, 100);

        var job = await FindLeasedJobAsync(jobId, workerId, cancellationToken);
        if (job is null || job.Status != BackgroundJobStatus.Running)
        {
            return false;
        }

        job.ProgressPercentage = Math.Min(progressPercentage, 99);
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        job.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsCancellationRequestedAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken)
    {
        var job = await FindLeasedJobAsync(jobId, workerId, cancellationToken);
        return job is null || job.Status == BackgroundJobStatus.CancellationRequested;
    }

    public async Task CompleteAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken)
    {
        var job = await FindLeasedJobAsync(jobId, workerId, cancellationToken);
        if (job is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = job.Status == BackgroundJobStatus.CancellationRequested
            ? BackgroundJobStatus.Cancelled
            : BackgroundJobStatus.Succeeded;
        if (job.Status == BackgroundJobStatus.Succeeded)
        {
            job.ProgressPercentage = 100;
        }
        job.CompletedAtUtc = now;
        job.UpdatedAtUtc = now;
        ClearLease(job);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken)
    {
        var job = await FindLeasedJobAsync(jobId, workerId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = BackgroundJobStatus.Cancelled;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ClearLease(job);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid jobId,
        string workerId,
        string failureCode,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        var job = await FindLeasedJobAsync(jobId, workerId, cancellationToken);
        if (job is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        job.FailureCode = failureCode;
        job.UpdatedAtUtc = now;
        ClearLease(job);

        if (job.Status == BackgroundJobStatus.CancellationRequested)
        {
            job.Status = BackgroundJobStatus.Cancelled;
            job.CompletedAtUtc = now;
        }
        else if (job.AttemptCount < job.MaxAttempts)
        {
            job.Status = BackgroundJobStatus.RetryWaiting;
            job.AvailableAtUtc = now.Add(retryDelay);
        }
        else
        {
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredJobs = await dbContext.BackgroundJobs
            .Where(job => (job.Status == BackgroundJobStatus.Running
                    || job.Status == BackgroundJobStatus.CancellationRequested)
                && job.LeaseExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var job in expiredJobs)
        {
            job.UpdatedAtUtc = now;
            ClearLease(job);

            if (job.Status == BackgroundJobStatus.CancellationRequested)
            {
                job.Status = BackgroundJobStatus.Cancelled;
                job.CompletedAtUtc = now;
            }
            else if (job.AttemptCount < job.MaxAttempts)
            {
                job.Status = BackgroundJobStatus.RetryWaiting;
                job.AvailableAtUtc = now.Add(GetRetryDelay(job.AttemptCount));
            }
            else
            {
                job.Status = BackgroundJobStatus.Failed;
                job.FailureCode = "worker_lease_expired";
                job.CompletedAtUtc = now;
            }
        }

        if (expiredJobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<BackgroundJob?> FindLeasedJobAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.BackgroundJobs.SingleOrDefaultAsync(
            job => job.Id == jobId && job.LeaseOwner == workerId,
            cancellationToken);
        if (job is not null)
        {
            await dbContext.Entry(job).ReloadAsync(cancellationToken);
        }

        return job;
    }

    private static TimeSpan GetRetryDelay(int attemptCount) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Max(0, attemptCount - 1))));

    private static void ClearLease(BackgroundJob job)
    {
        job.LeaseOwner = null;
        job.LeaseExpiresAtUtc = null;
    }
}
