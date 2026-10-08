using System.ComponentModel.DataAnnotations;

namespace RepoAI.Api.Contracts;

public sealed class GoogleSignInRequest
{
    [Required]
    [StringLength(8192, MinimumLength = 20)]
    public required string IdToken { get; init; }
}
