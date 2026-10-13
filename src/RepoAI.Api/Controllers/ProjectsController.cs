using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;
using RepoAI.Application.Projects;
using RepoAI.Domain.Entities;

namespace RepoAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
[Tags("Projects")]
public sealed class ProjectsController(ProjectService projectService) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [EndpointSummary("Create a project for the signed-in user")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProjectResponse>> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var normalizedRepositoryUrl = new Uri(request.RepositoryUrl, UriKind.Absolute).AbsoluteUri;
        var project = await projectService.CreateAsync(
            ownerId,
            request.Name,
            normalizedRepositoryUrl,
            cancellationToken);
        var response = Map(project);

        return CreatedAtAction(nameof(GetById), new { projectId = project.Id }, response);
    }

    [HttpGet]
    [EndpointSummary("List projects belonging to the signed-in user")]
    [ProducesResponseType<IReadOnlyList<ProjectResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> List(
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var projects = await projectService.ListForOwnerAsync(ownerId, cancellationToken);
        return Ok(projects.Select(Map).ToArray());
    }

    [HttpGet("{projectId:guid}")]
    [EndpointSummary("Get a project belonging to the signed-in user")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> GetById(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var project = await projectService.FindForOwnerAsync(
            ownerId,
            projectId,
            cancellationToken);

        return project is null ? NotFound() : Ok(Map(project));
    }

    [HttpDelete("{projectId:guid}")]
    [EndpointSummary("Delete an owned project and all its repository data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var result = await projectService.DeleteForOwnerAsync(ownerId, projectId, cancellationToken);
        return result switch
        {
            ProjectDeletionResult.Deleted => NoContent(),
            ProjectDeletionResult.NotFound => NotFound(),
            ProjectDeletionResult.ActiveJobs => Conflict(new ProblemDetails
            {
                Title = "Project has active background jobs",
                Detail = "Wait for the project's running jobs to finish or cancel them before deleting the project."
            }),
            _ => throw new InvalidOperationException("Unknown project deletion result.")
        };
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var subject = User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static ProjectResponse Map(Project project) => new(
        project.Id,
        project.Name,
        project.RepositoryUrl,
        project.CreatedAtUtc);
}
