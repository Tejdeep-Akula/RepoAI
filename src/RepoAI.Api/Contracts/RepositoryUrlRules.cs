namespace RepoAI.Api.Contracts;

internal static class RepositoryUrlRules
{
    public const string ErrorMessage =
        "Use an absolute HTTPS URL with a hostname and repository path, without credentials, a custom port, query, or fragment.";

    public static bool IsValid(string repositoryUrl) =>
        Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.HostNameType == UriHostNameType.Dns
        && !string.IsNullOrWhiteSpace(uri.Host)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath.Trim('/').Length > 0;
}
