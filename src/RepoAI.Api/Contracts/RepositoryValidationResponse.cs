namespace RepoAI.Api.Contracts;

public sealed record RepositoryValidationResponse(
    string RepositoryUrl,
    string Host,
    bool ReachabilityChecked);
