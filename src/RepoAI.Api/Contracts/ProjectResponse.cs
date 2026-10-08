namespace RepoAI.Api.Contracts;

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string RepositoryUrl,
    DateTimeOffset CreatedAtUtc);
