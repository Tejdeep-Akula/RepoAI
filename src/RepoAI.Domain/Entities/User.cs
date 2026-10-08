namespace RepoAI.Domain.Entities;

/// <summary>
/// A RepoAI account linked to a verified Google identity.
/// </summary>
public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string GoogleSubject { get; set; }

    public required string DisplayName { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
