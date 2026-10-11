namespace RepoAI.Application.Repositories;

public sealed record GitHubRepositoryReference(string Owner, string Name)
{
    public static bool TryParse(string? repositoryUrl, out GitHubRepositoryReference? reference)
    {
        reference = null;
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var owner = Uri.UnescapeDataString(parts[0]);
        var repositoryName = Uri.UnescapeDataString(parts[1]);
        if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryName = repositoryName[..^4];
        }

        if (!IsSafeSegment(owner) || !IsSafeSegment(repositoryName))
        {
            return false;
        }

        reference = new GitHubRepositoryReference(owner, repositoryName);
        return true;
    }

    private static bool IsSafeSegment(string value) =>
        value.Length is > 0 and <= 100
        && value is not "." and not ".."
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');
}
