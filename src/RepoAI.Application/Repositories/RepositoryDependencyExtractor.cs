using System.Text;
using RepoAI.Domain.Entities;

namespace RepoAI.Application.Repositories;

/// <summary>Loads stored manifest text and delegates dependency extraction to registered parsers.</summary>
public sealed class RepositoryDependencyExtractor(
    IRepositoryContentStore contentStore,
    IEnumerable<IRepositoryManifestDependencyParser> parsers)
{
    private const long MaximumManifestBytes = 2 * 1024 * 1024;

    public async Task<IReadOnlyList<DeclaredRepositoryDependency>> ExtractAsync(
        IReadOnlyCollection<RepositoryFile> files,
        CancellationToken cancellationToken)
    {
        var results = new List<DeclaredRepositoryDependency>();
        foreach (var file in files)
        {
            if (file.ContentStorageKey is null || file.IsBinary || file.SizeBytes > MaximumManifestBytes)
            {
                continue;
            }

            var parser = parsers.FirstOrDefault(candidate => candidate.CanParse(file.RelativePath));
            if (parser is null) continue;

            await using var stream = await contentStore.OpenReadAsync(file.ContentStorageKey, cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            try
            {
                results.AddRange(parser.Parse(file.RelativePath, content));
            }
            catch (FormatException)
            {
                // A malformed manifest should not prevent analysis of the remaining repository.
            }
            catch (System.Text.Json.JsonException)
            {
                // A malformed manifest should not prevent analysis of the remaining repository.
            }
            catch (System.Xml.XmlException)
            {
                // A malformed manifest should not prevent analysis of the remaining repository.
            }
        }

        return results;
    }
}
