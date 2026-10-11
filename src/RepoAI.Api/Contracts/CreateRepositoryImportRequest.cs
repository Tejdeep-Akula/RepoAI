using System.ComponentModel.DataAnnotations;

namespace RepoAI.Api.Contracts;

public sealed class CreateRepositoryImportRequest
{
    [StringLength(255)]
    public string? Ref { get; init; }
}
