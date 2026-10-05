using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;

namespace RepoAI.Api.Controllers;

[ApiController]
[Route("api/v1/repositories")]
[Tags("Repositories")]
public sealed class RepositoriesController : ControllerBase
{
    [HttpPost("validate")]
    [Consumes("application/json")]
    [EndpointSummary("Validate a repository URL")]
    [EndpointDescription("Validates URL syntax and returns its normalized form. No network request, import, persistence, provider support check, or access check is performed. This is not an SSRF security check for future imports.")]
    [ProducesResponseType<RepositoryValidationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public ActionResult<RepositoryValidationResponse> Validate(ValidateRepositoryRequest request)
    {
        var uri = new Uri(request.RepositoryUrl, UriKind.Absolute);
        return Ok(new RepositoryValidationResponse(uri.AbsoluteUri, uri.Host, false));
    }
}
