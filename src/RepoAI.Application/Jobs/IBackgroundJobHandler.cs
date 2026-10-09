using RepoAI.Domain.Entities;

namespace RepoAI.Application.Jobs;

public interface IBackgroundJobHandler
{
    string Kind { get; }

    Task ExecuteAsync(
        BackgroundJob job,
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken);
}

public interface IBackgroundJobExecutionContext
{
    Task ReportProgressAsync(int progressPercentage, CancellationToken cancellationToken);
}
