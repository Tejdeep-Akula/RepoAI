using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RepoAI.Application.Chat;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Repositories;

/// <summary>Bounded lexical retrieval over the immutable text files in one repository snapshot.</summary>
public sealed partial class RepositoryEvidenceRetriever(
    IRepositorySnapshotRepository snapshotRepository,
    IRepositoryContentStore contentStore,
    ILogger<RepositoryEvidenceRetriever> logger) : IRepositoryEvidenceRetriever
{
    private const int MaxCandidateFiles = 300;
    private const int MaxFileBytes = 512 * 1024;
    private const long MaxReadBytes = 24 * 1024 * 1024;
    private const int MaxEvidenceChunks = 8;
    private const int ChunkLineCount = 64;
    private const int ChunkOverlap = 12;
    private const int MaxChunkCharacters = 7_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "and", "are", "does", "from", "have", "into", "its",
        "just", "not", "our", "the", "their", "them", "then", "this", "that", "what",
        "when", "where", "which", "with", "would", "your", "you", "how", "can", "use",
        "using", "used", "for", "was", "were", "will", "why", "who", "all", "any"
    };

    public async Task<IReadOnlyList<RepositoryEvidenceChunk>> RetrieveAsync(
        Guid snapshotId,
        string question,
        CancellationToken cancellationToken)
    {
        var terms = TokenRegex().Matches(question)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(term => term.Length >= 3 && !StopWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        var candidates = (await snapshotRepository.ListAllFilesAsync(snapshotId, cancellationToken))
            .Where(file => file.ContentStorageKey is not null
                && !file.IsBinary
                && file.SizeBytes is > 0 and <= MaxFileBytes)
            .Select(file => new FileCandidate(file, ScorePath(file.RelativePath, terms), GetPathPriority(file.RelativePath)))
            .OrderByDescending(candidate => candidate.PathScore)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.File.RelativePath, StringComparer.Ordinal)
            .Take(MaxCandidateFiles)
            .ToArray();

        var chunks = new List<RepositoryEvidenceChunk>();
        long bytesRead = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytesRead + candidate.File.SizeBytes > MaxReadBytes)
            {
                continue;
            }

            try
            {
                await using var stream = await contentStore.OpenReadAsync(candidate.File.ContentStorageKey!, cancellationToken);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                var content = await reader.ReadToEndAsync(cancellationToken);
                bytesRead += stream.CanSeek ? stream.Length : System.Text.Encoding.UTF8.GetByteCount(content);
                chunks.AddRange(ScoreChunks(candidate.File, content, terms, candidate.PathScore, candidate.Priority));
            }
            catch (FileNotFoundException exception)
            {
                logger.LogWarning(exception, "Stored repository content is missing for {RelativePath}", candidate.File.RelativePath);
            }
            catch (DirectoryNotFoundException exception)
            {
                logger.LogWarning(exception, "Stored repository content directory is missing for {RelativePath}", candidate.File.RelativePath);
            }
        }

        var selected = chunks
            .OrderByDescending(chunk => chunk.RelevanceScore)
            .ThenBy(chunk => chunk.Path, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.StartLine)
            .Take(MaxEvidenceChunks)
            .ToArray();

        logger.LogInformation(
            "Retrieved {EvidenceCount} repository evidence chunks from {CandidateFileCount} candidate files for snapshot {SnapshotId}",
            selected.Length,
            candidates.Length,
            snapshotId);
        return selected;
    }

    private static IEnumerable<RepositoryEvidenceChunk> ScoreChunks(
        RepositoryFile file,
        string content,
        IReadOnlyList<string> terms,
        int pathScore,
        int priority)
    {
        var lines = content.Split('\n');
        for (var offset = 0; offset < lines.Length; offset += ChunkLineCount - ChunkOverlap)
        {
            var length = Math.Min(ChunkLineCount, lines.Length - offset);
            var snippet = string.Join('\n', lines.AsSpan(offset, length).ToArray()).Trim();
            if (snippet.Length == 0)
            {
                continue;
            }

            if (snippet.Length > MaxChunkCharacters)
            {
                snippet = snippet[..MaxChunkCharacters];
            }

            var score = pathScore + priority;
            foreach (var term in terms)
            {
                var occurrences = TokenRegex().Matches(snippet)
                    .Count(match => string.Equals(match.Value, term, StringComparison.OrdinalIgnoreCase));
                score += Math.Min(occurrences, 6);
            }

            if (score == 0)
            {
                continue;
            }

            yield return new RepositoryEvidenceChunk(
                file.RelativePath,
                offset + 1,
                offset + length,
                snippet,
                score);
        }
    }

    private static int ScorePath(string path, IReadOnlyList<string> terms)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        return terms.Count(term => normalized.Contains(term, StringComparison.Ordinal)) * 8;
    }

    private static int GetPathPriority(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase)
            || name.Equals("README", StringComparison.OrdinalIgnoreCase)) return 8;
        if (name is "Program.cs" or "Program.csx" or "main.py" or "main.go" or "main.ts" or "main.js") return 6;
        if (name is "package.json" or "pyproject.toml" or "go.mod" or "Cargo.toml" or "pom.xml") return 5;
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
            || name.Equals("compose.yaml", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".go", StringComparison.OrdinalIgnoreCase)) return 2;
        return 1;
    }

    [GeneratedRegex("[\\p{L}\\p{N}_]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private sealed record FileCandidate(RepositoryFile File, int PathScore, int Priority);
}
