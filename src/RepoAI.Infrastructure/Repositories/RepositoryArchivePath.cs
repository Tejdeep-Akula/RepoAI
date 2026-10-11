using RepoAI.Application.Jobs;

namespace RepoAI.Infrastructure.Repositories;

public static class RepositoryArchivePath
{
    public static string? NormalizeGitHubEntry(string entryName, string? rootPrefix)
    {
        if (entryName.StartsWith('/') || entryName.Contains('\\'))
        {
            throw new BackgroundJobFailureException("repository_archive_invalid", "The archive contains an unsafe file path.");
        }

        var segments = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new BackgroundJobFailureException("repository_archive_invalid", "The archive contains an unsafe file path.");
        }

        if (segments.Length > 0 && rootPrefix is not null && segments[0] == rootPrefix)
        {
            segments = segments[1..];
        }

        return segments.Length == 0 ? null : string.Join('/', segments);
    }
}
