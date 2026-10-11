namespace RepoAI.Application.Chat;

public interface IRepositoryChatModel
{
    string ModelId { get; }

    IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ChatPromptMessage> messages,
        CancellationToken cancellationToken);
}
