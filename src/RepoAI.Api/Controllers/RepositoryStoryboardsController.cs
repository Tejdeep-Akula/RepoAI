using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;
using RepoAI.Application.Jobs;
using RepoAI.Application.Projects;
using RepoAI.Application.Repositories;
using RepoAI.Application.Storyboards;
using RepoAI.Domain.Entities;

namespace RepoAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/snapshots/{snapshotId:guid}/storyboard")]
[Tags("Repository storyboards")]
public sealed class RepositoryStoryboardsController(
    ProjectService projectService,
    IRepositorySnapshotRepository snapshotRepository,
    IRepositoryArchitectureAnalysisRepository analysisRepository,
    IRepositoryStoryboardRepository storyboardRepository,
    BackgroundJobService jobService) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    [EndpointSummary("Queue evidence-grounded storyboard generation for an analyzed snapshot")]
    [ProducesResponseType<BackgroundJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BackgroundJobResponse>> Generate(
        Guid projectId,
        Guid snapshotId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId)) return Unauthorized();
        if (await projectService.FindForOwnerAsync(ownerId, projectId, cancellationToken) is null)
            return NotFound();
        var snapshot = await snapshotRepository.FindForProjectOwnerAsync(ownerId, projectId, snapshotId, cancellationToken);
        if (snapshot is null) return NotFound();
        if (await analysisRepository.FindForOwnerAsync(ownerId, projectId, snapshotId, cancellationToken) is null)
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Architecture analysis required",
                Detail = "Import the repository and complete architecture analysis before generating a storyboard."
            });

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > 128 })
            return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid Idempotency-Key", Detail = "The idempotency key cannot exceed 128 characters." });
        var job = await jobService.EnqueueAsync(projectId, BackgroundJobKinds.RepositoryStoryboard,
            new RepositoryStoryboardJobPayload(snapshotId), key, cancellationToken);
        return Accepted($"/api/v1/jobs/{job.Id}", new BackgroundJobResponse(
            job.Id, job.ProjectId, job.Kind, job.Status.ToString(), job.ProgressPercentage, job.AttemptCount,
            job.MaxAttempts, job.CreatedAtUtc, job.StartedAtUtc, job.CompletedAtUtc, job.FailureCode, job.FailureMessage));
    }

    [HttpGet]
    [EndpointSummary("Get the saved storyboard for an analyzed snapshot")]
    [ProducesResponseType<RepositoryStoryboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepositoryStoryboardResponse>> Get(
        Guid projectId, Guid snapshotId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId)) return Unauthorized();
        if (await projectService.FindForOwnerAsync(ownerId, projectId, cancellationToken) is null)
            return NotFound();
        var snapshot = await snapshotRepository.FindForProjectOwnerAsync(ownerId, projectId, snapshotId, cancellationToken);
        if (snapshot is null) return NotFound();
        var saved = await storyboardRepository.FindForOwnerAsync(ownerId, projectId, snapshotId, cancellationToken);
        if (saved is null) return NotFound();
        var storyboard = JsonSerializer.Deserialize<RepositoryStoryboard>(saved.StoryboardJson, JsonOptions)
            ?? throw new InvalidOperationException("The saved repository storyboard is invalid.");
        return Ok(new RepositoryStoryboardResponse(saved.SnapshotId, saved.CommitSha, saved.Model,
            saved.GeneratedAtUtc, storyboard));
    }

    private bool TryGetCurrentUserId(out Guid userId) => Guid.TryParse(User.FindFirst("sub")?.Value, out userId);
}

public sealed record RepositoryStoryboardResponse(
    Guid SnapshotId,
    string CommitSha,
    string Model,
    DateTimeOffset GeneratedAtUtc,
    RepositoryStoryboard Storyboard);
