using System.ComponentModel.DataAnnotations;

namespace RepoAI.Api.Contracts;

/// <summary>Checks URL syntax only. This does not contact a repository provider.</summary>
public sealed class ValidateRepositoryRequest : IValidatableObject
{
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string RepositoryUrl { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(RepositoryUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.HostNameType != UriHostNameType.Dns
            || string.IsNullOrWhiteSpace(uri.Host)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath.Trim('/').Length == 0)
        {
            yield return new ValidationResult(
                "Use an absolute HTTPS URL with a hostname and repository path, without credentials, a custom port, query, or fragment.",
                [nameof(RepositoryUrl)]);
        }
    }
}
