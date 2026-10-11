using Microsoft.EntityFrameworkCore;
using RepoAI.Application.Chat;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class ChatConversationRepository(RepoAIDbContext dbContext) : IChatConversationRepository
{
    public async Task<ChatConversation?> CreateForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid snapshotId,
        string title,
        CancellationToken cancellationToken)
    {
        var snapshotIsOwned = await dbContext.RepositorySnapshots.AnyAsync(
            snapshot => snapshot.Id == snapshotId
                && snapshot.ProjectId == projectId
                && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId),
            cancellationToken);
        if (!snapshotIsOwned)
        {
            return null;
        }

        var conversation = new ChatConversation
        {
            ProjectId = projectId,
            SnapshotId = snapshotId,
            Title = title
        };
        dbContext.ChatConversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<IReadOnlyList<ChatConversation>> ListForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken) =>
        await dbContext.ChatConversations
            .AsNoTracking()
            .Where(conversation => conversation.ProjectId == projectId
                && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId))
            .OrderByDescending(conversation => conversation.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<ChatConversation?> FindForProjectOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        dbContext.ChatConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                conversation => conversation.Id == conversationId
                    && conversation.ProjectId == projectId
                    && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId),
                cancellationToken);

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        await dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId
                && dbContext.ChatConversations.Any(conversation => conversation.Id == conversationId
                    && conversation.ProjectId == projectId
                    && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId)))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

    public async Task<ChatMessage?> AppendMessageForOwnerAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string role,
        string content,
        string? model,
        string? citationsJson,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.ChatConversations
            .SingleOrDefaultAsync(
                item => item.Id == conversationId
                    && item.ProjectId == projectId
                    && dbContext.Projects.Any(project => project.Id == projectId && project.OwnerId == ownerId),
                cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model,
            CitationsJson = citationsJson
        };
        dbContext.ChatMessages.Add(message);
        conversation.UpdatedAtUtc = message.CreatedAtUtc;

        if (role == "user" && conversation.Title == "Repository questions"
            && !await dbContext.ChatMessages.AnyAsync(
                existing => existing.ConversationId == conversationId && existing.Role == "user",
                cancellationToken))
        {
            var firstLine = content.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                conversation.Title = firstLine.Length > 72 ? $"{firstLine[..69]}…" : firstLine;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }
}
