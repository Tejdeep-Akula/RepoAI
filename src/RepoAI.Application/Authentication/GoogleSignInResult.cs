namespace RepoAI.Application.Authentication;

public sealed record GoogleSignInResult(
    Guid UserId,
    string DisplayName,
    string? Email,
    IssuedAccessToken AccessToken);
