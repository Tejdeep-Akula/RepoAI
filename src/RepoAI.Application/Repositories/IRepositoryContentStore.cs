namespace RepoAI.Application.Repositories;

public interface IRepositoryContentStore
{
    Task<string> SaveTextFileAsync(
        Guid snapshotId,
        string sha256,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    void DeleteSnapshot(Guid snapshotId);
}
