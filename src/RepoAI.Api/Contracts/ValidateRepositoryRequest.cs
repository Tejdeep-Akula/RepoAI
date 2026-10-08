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
        if (!RepositoryUrlRules.IsValid(RepositoryUrl))
        {
            yield return new ValidationResult(
                RepositoryUrlRules.ErrorMessage,
                [nameof(RepositoryUrl)]);
        }
    }
}
