using System.ComponentModel.DataAnnotations;

namespace RepoAI.Api.Contracts;

public sealed class CreateProjectRequest : IValidatableObject
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string RepositoryUrl { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Project name cannot be empty or whitespace.",
                [nameof(Name)]);
        }

        if (!RepositoryUrlRules.IsValid(RepositoryUrl))
        {
            yield return new ValidationResult(
                RepositoryUrlRules.ErrorMessage,
                [nameof(RepositoryUrl)]);
        }
    }
}
