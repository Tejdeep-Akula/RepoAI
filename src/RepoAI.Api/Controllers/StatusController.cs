using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;

namespace RepoAI.Api.Controllers;

[ApiController]
[Route("api/v1/status")]
[Tags("Status")]
public sealed class StatusController : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Get API status")]
    [EndpointDescription("Reports API availability only, not database or model readiness.")]
    [ProducesResponseType<ApiStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<ApiStatusResponse> Get() =>
        Ok(new ApiStatusResponse("RepoAI", "API contracts ready"));

    [HttpGet("/")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult<ApiStatusResponse> Root() => Get();
}
