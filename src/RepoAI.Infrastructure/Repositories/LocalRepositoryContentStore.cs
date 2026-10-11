using Microsoft.Extensions.Options;
using RepoAI.Application.Repositories;

namespace RepoAI.Infrastructure.Repositories;

public sealed class LocalRepositoryContentStore(IOptions<RepositoryImportOptions> options) : IRepositoryContentStore
{
    public async Task<string> SaveTextFileAsync(
        Guid snapshotId,
        string sha256,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{snapshotId:N}/{sha256}.txt";
        var path = GetAbsolutePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken);
        return storageKey;
    }

    public void DeleteSnapshot(Guid snapshotId)
    {
        var path = Path.GetFullPath(Path.Combine(options.Value.StorageRoot, snapshotId.ToString("N")));
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            GetAbsolutePath(storageKey),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private string GetAbsolutePath(string storageKey)
    {
        var root = Path.GetFullPath(options.Value.StorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository content storage key escaped its configured root.");
        }

        return path;
    }
}
