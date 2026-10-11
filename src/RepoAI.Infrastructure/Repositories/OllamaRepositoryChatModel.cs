using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RepoAI.Application.Chat;
using ModelChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace RepoAI.Infrastructure.Repositories;

public sealed class OllamaRepositoryChatModel(
    IChatClient chatClient,
    IOptions<OllamaChatOptions> options) : IRepositoryChatModel
{
    public string ModelId => options.Value.Model;

    public async IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ChatPromptMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var history = messages.Select(message => new ModelChatMessage(
            message.Role switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            },
            message.Content)).ToArray();

        var chatOptions = new ChatOptions
        {
            ModelId = options.Value.Model,
            Temperature = 0.2f,
            MaxOutputTokens = 2_048
        };

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            history,
            chatOptions,
            cancellationToken).WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
