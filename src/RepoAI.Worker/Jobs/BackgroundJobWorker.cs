using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RepoAI.Application.Jobs;
using RepoAI.Domain.Entities;

namespace RepoAI.Worker.Jobs;

public sealed class BackgroundJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private readonly string workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RepoAI background worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IBackgroundJobRepository>();
                var handlers = scope.ServiceProvider.GetServices<IBackgroundJobHandler>().ToArray();
                var supportedKinds = handlers.Select(handler => handler.Kind).ToArray();
                var job = await repository.ClaimNextAsync(
                    workerId,
                    supportedKinds,
                    LeaseDuration,
                    stoppingToken);
                if (job is null)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                await ProcessAsync(job, repository, handlers, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job polling failed");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }

        logger.LogInformation("RepoAI background worker {WorkerId} stopped", workerId);
    }

    private async Task ProcessAsync(
        BackgroundJob job,
        IBackgroundJobRepository repository,
        IReadOnlyCollection<IBackgroundJobHandler> handlers,
        CancellationToken stoppingToken)
    {
        var handler = handlers
            .SingleOrDefault(candidate => candidate.Kind == job.Kind);
        if (handler is null) return;

        var executionContext = new BackgroundJobExecutionContext(repository, job.Id, workerId, LeaseDuration);
        try
        {
            await handler.ExecuteAsync(job, executionContext, stoppingToken);
            await repository.CompleteAsync(job.Id, workerId, stoppingToken);
            logger.LogInformation("Background job {JobId} completed", job.Id);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The lease expires and another worker safely retries the job after a process restart.
            throw;
        }
        catch (OperationCanceledException)
        {
            await repository.CancelAsync(job.Id, workerId, CancellationToken.None);
            logger.LogInformation("Background job {JobId} was cancelled", job.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Background job {JobId} failed", job.Id);
            await repository.FailAsync(
                job.Id,
                workerId,
                "handler_failed",
                TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Max(0, job.AttemptCount - 1)))),
                stoppingToken);
        }
    }

    private sealed class BackgroundJobExecutionContext(
        IBackgroundJobRepository repository,
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration) : IBackgroundJobExecutionContext
    {
        public async Task ReportProgressAsync(int progressPercentage, CancellationToken cancellationToken)
        {
            var leaseRenewed = await repository.ReportProgressAndRenewLeaseAsync(
                jobId,
                workerId,
                progressPercentage,
                leaseDuration,
                cancellationToken);
            if (!leaseRenewed)
            {
                throw new OperationCanceledException("The job was cancelled or its worker lease expired.");
            }
        }
    }
}
