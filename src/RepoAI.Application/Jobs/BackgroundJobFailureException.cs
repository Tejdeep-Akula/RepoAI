namespace RepoAI.Application.Jobs;

public sealed class BackgroundJobFailureException(string failureCode, string message) : Exception(message)
{
    public string FailureCode { get; } = failureCode;
}
