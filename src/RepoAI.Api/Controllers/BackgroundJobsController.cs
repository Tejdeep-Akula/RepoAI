using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;
using RepoAI.Application.Jobs;
using RepoAI.Application.Projects;
using RepoAI.Domain.Entities;

namespace RepoAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
[Tags("Background jobs")]
public sealed class BackgroundJobsController(
    BackgroundJobService jobService,
    ProjectService projectService,
    IHostEnvironment environment) : ControllerBase
{
    [HttpPost("projects/{projectId:guid}/jobs/demo")]
    [EndpointSummary("Queue a development-only job to verify durable processing")]
    [ProducesResponseType<BackgroundJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BackgroundJobResponse>> CreateDemo(
        Guid projectId,
        CreateDemoJobRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? requestIdempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var project = await projectService.FindForOwnerAsync(ownerId, projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var idempotencyKey = requestIdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            idempotencyKey = null;
        }
        if (idempotencyKey is { Length: > 128 })
        {
            ModelState.AddModelError("Idempotency-Key", "The idempotency key cannot exceed 128 characters.");
            return ValidationProblem(ModelState);
        }

        var job = await jobService.EnqueueAsync(
            projectId,
            BackgroundJobKinds.DemoProgress,
            new { request.FailFirstAttempt, request.StepDelayMilliseconds },
            idempotencyKey,
            cancellationToken);

        return AcceptedAtAction(nameof(GetById), new { jobId = job.Id }, Map(job));
    }

    [HttpGet("jobs/{jobId:guid}")]
    [EndpointSummary("Get the status of a job belonging to the signed-in user")]
    [ProducesResponseType<BackgroundJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BackgroundJobResponse>> GetById(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var job = await jobService.FindForOwnerAsync(ownerId, jobId, cancellationToken);
        return job is null ? NotFound() : Ok(Map(job));
    }

    [HttpDelete("jobs/{jobId:guid}")]
    [EndpointSummary("Request cancellation of a job belonging to the signed-in user")]
    [ProducesResponseType<BackgroundJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BackgroundJobResponse>> Cancel(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var job = await jobService.RequestCancellationAsync(ownerId, jobId, cancellationToken);
        return job is null ? NotFound() : Accepted(Map(job));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var subject = User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static BackgroundJobResponse Map(BackgroundJob job) => new(
        job.Id,
        job.ProjectId,
        job.Kind,
        job.Status.ToString(),
        job.ProgressPercentage,
        job.AttemptCount,
        job.MaxAttempts,
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.CompletedAtUtc,
        job.FailureCode,
        job.FailureMessage);
}
