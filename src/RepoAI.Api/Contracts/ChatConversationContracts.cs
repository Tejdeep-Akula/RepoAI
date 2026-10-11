using System.ComponentModel.DataAnnotations;

namespace RepoAI.Api.Contracts;

public sealed class CreateChatConversationRequest
{
    [Required]
    public Guid SnapshotId { get; init; }

    [StringLength(160, MinimumLength = 1)]
    public string? Title { get; init; }
}

public sealed class CreateChatMessageRequest
{
    [Required]
    [StringLength(8_000, MinimumLength = 1)]
    public string Content { get; init; } = string.Empty;
}

public sealed record ChatConversationResponse(
    Guid Id,
    Guid ProjectId,
    Guid SnapshotId,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChatMessageResponse(
    Guid Id,
    string Role,
    string Content,
    string? Model,
    IReadOnlyList<ChatCitationResponse> Citations,
    DateTimeOffset CreatedAtUtc);

public sealed record ChatCitationResponse(string Id, string Path, int StartLine, int EndLine);

public sealed record ChatConversationDetailResponse(
    ChatConversationResponse Conversation,
    IReadOnlyList<ChatMessageResponse> Messages);
