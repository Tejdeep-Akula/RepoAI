namespace RepoAI.Application.Chat;

public sealed record ChatPromptMessage(string Role, string Content);

public sealed record RepositoryChatCitation(string Id, string Path, int StartLine, int EndLine);

public sealed record RepositoryEvidenceChunk(
    string Path,
    int StartLine,
    int EndLine,
    string Content,
    int RelevanceScore);

public sealed record ChatStreamEvent(
    string Type,
    string? Text = null,
    Guid? MessageId = null,
    IReadOnlyList<RepositoryChatCitation>? Citations = null,
    string? Code = null,
    string? Error = null);
