using System.ComponentModel.DataAnnotations;

namespace RepoAI.Api.Contracts;

public sealed class CreateDemoJobRequest
{
    public bool FailFirstAttempt { get; init; }

    [Range(0, 5000)]
    public int StepDelayMilliseconds { get; init; } = 1000;
}
