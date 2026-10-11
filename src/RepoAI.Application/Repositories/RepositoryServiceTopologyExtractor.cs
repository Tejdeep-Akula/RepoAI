using System.Text;
using RepoAI.Domain.Entities;
using YamlDotNet.Core;

namespace RepoAI.Application.Repositories;

/// <summary>Loads stored deployment manifests and extracts explicit service relationships.</summary>
public sealed class RepositoryServiceTopologyExtractor(
    IRepositoryContentStore contentStore,
    IEnumerable<IRepositoryServiceTopologyParser> parsers)
{
    private const long MaximumManifestBytes = 2 * 1024 * 1024;

    public async Task<IReadOnlyList<DeclaredServiceRelationship>> ExtractAsync(
        IReadOnlyCollection<RepositoryFile> files,
        CancellationToken cancellationToken)
    {
        var results = new List<DeclaredServiceRelationship>();
        foreach (var file in files)
        {
            if (file.ContentStorageKey is null || file.IsBinary || file.SizeBytes > MaximumManifestBytes) continue;
            var parser = parsers.FirstOrDefault(candidate => candidate.CanParse(file.RelativePath));
            if (parser is null) continue;

            await using var stream = await contentStore.OpenReadAsync(file.ContentStorageKey, cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            try
            {
                results.AddRange(parser.Parse(file.RelativePath, content));
            }
            catch (YamlException)
            {
                // A malformed Compose manifest must not prevent analysis of other repository evidence.
            }
        }

        return results;
    }
}
