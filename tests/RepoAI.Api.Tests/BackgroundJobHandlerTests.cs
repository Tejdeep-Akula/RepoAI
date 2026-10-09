extern alias Worker;

using RepoAI.Application.Jobs;
using RepoAI.Domain.Entities;
using Worker::RepoAI.Worker.Jobs;
using Xunit;

namespace RepoAI.Api.Tests;

public sealed class BackgroundJobHandlerTests
{
    [Fact]
    public async Task DemoHandlerReportsIncrementalProgress()
    {
        var handler = new DemoProgressJobHandler();
        var context = new RecordingExecutionContext();
        var job = new BackgroundJob
        {
            ProjectId = Guid.NewGuid(),
            Kind = BackgroundJobKinds.DemoProgress,
            PayloadJson = "{\"stepDelayMilliseconds\":0}"
        };

        await handler.ExecuteAsync(job, context, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 10).Select(step => step * 10), context.ProgressValues);
    }

    [Fact]
    public async Task DemoHandlerCanFailOnceToExerciseRetry()
    {
        var handler = new DemoProgressJobHandler();
        var context = new RecordingExecutionContext();
        var job = new BackgroundJob
        {
            ProjectId = Guid.NewGuid(),
            Kind = BackgroundJobKinds.DemoProgress,
            PayloadJson = "{\"failFirstAttempt\":true,\"stepDelayMilliseconds\":0}",
            AttemptCount = 1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(job, context, CancellationToken.None));
        Assert.Empty(context.ProgressValues);
    }

    private sealed class RecordingExecutionContext : IBackgroundJobExecutionContext
    {
        public List<int> ProgressValues { get; } = [];

        public Task ReportProgressAsync(int progressPercentage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProgressValues.Add(progressPercentage);
            return Task.CompletedTask;
        }
    }
}
