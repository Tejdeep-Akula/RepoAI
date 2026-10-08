namespace RepoAI.Application.Authentication;

public sealed record GoogleIdentity(string Subject, string DisplayName, string? Email);
