namespace RepoAI.Infrastructure.Repositories;

public sealed class OllamaChatOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "qwen2.5-coder:7b";
}
