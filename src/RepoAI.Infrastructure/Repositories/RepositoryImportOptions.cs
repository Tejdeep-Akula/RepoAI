namespace RepoAI.Infrastructure.Repositories;

public sealed class RepositoryImportOptions
{
    public string StorageRoot { get; set; } = string.Empty;

    public long MaxArchiveBytes { get; set; } = 100 * 1024 * 1024;

    public long MaxExpandedBytes { get; set; } = 500 * 1024 * 1024;

    public long MaxTextFileBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxFiles { get; set; } = 20_000;

    public int MaxRefLength { get; set; } = 255;
}
