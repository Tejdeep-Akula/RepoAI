namespace RepoAI.Application.Authentication;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc);
