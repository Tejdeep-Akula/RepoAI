namespace RepoAI.Api.Contracts;

public sealed record GoogleSignInResponse(
    Guid UserId,
    string DisplayName,
    string? Email,
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc);
