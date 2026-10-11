using RepoAI.Domain.Entities;

namespace RepoAI.Application.Chat;

public interface IChatConversationRepository
{
    Task<ChatConversation?> CreateForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        string title,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatConversation>> ListForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ChatConversation?> FindForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> ListMessagesForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<ChatMessage?> AppendMessageForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string role,
        string content,
        string? model,
        string? citationsJson,
        CancellationToken cancellationToken);
}
