using System.Text.Json;
using RepoAI.Application.Jobs;
using RepoAI.Domain.Entities;

namespace RepoAI.Worker.Jobs;

public sealed class DemoProgressJobHandler : IBackgroundJobHandler
{
    public string Kind => BackgroundJobKinds.DemoProgress;

    public async Task ExecuteAsync(
        BackgroundJob job,
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(job.PayloadJson);
        var failFirstAttempt = payload.RootElement.TryGetProperty("failFirstAttempt", out var flag)
            && flag.GetBoolean();
        var stepDelayMilliseconds = payload.RootElement.TryGetProperty("stepDelayMilliseconds", out var delay)
            ? delay.GetInt32()
            : 1000;
        ArgumentOutOfRangeException.ThrowIfNegative(stepDelayMilliseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stepDelayMilliseconds, 5000);
        if (failFirstAttempt && job.AttemptCount == 1)
        {
            throw new InvalidOperationException("Requested demo retry.");
        }

        const int stepCount = 10;
        for (var step = 1; step <= stepCount; step++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(stepDelayMilliseconds), cancellationToken);
            await executionContext.ReportProgressAsync(step * 10, cancellationToken);
        }
    }
}
