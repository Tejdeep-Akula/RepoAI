using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;
using RepoAI.Application.Jobs;
using RepoAI.Application.Projects;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}")]
[Tags("Repository imports")]
public sealed class RepositoryImportsController(
    BackgroundJobService jobService,
    ProjectService projectService,
    IRepositorySnapshotRepository snapshotRepository) : ControllerBase
{
    [HttpPost("imports")]
    [Consumes("application/json")]
    [EndpointSummary("Queue an import of a public GitHub repository")]
    [ProducesResponseType<BackgroundJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BackgroundJobResponse>> CreateImport(
        Guid projectId,
        CreateRepositoryImportRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? requestIdempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var project = await projectService.FindForOwnerAsync(ownerId, projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!GitHubRepositoryReference.TryParse(project.RepositoryUrl, out _))
        {
            ModelState.AddModelError(
                "repositoryUrl",
                "Repository imports currently support public github.com owner/repository URLs only.");
            return ValidationProblem(ModelState);
        }

        var requestedRef = string.IsNullOrWhiteSpace(request.Ref) ? null : request.Ref.Trim();
        if (requestedRef?.Any(char.IsControl) == true)
        {
            ModelState.AddModelError("ref", "The repository ref cannot contain control characters.");
            return ValidationProblem(ModelState);
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
            BackgroundJobKinds.RepositoryImport,
            new RepositoryImportJobPayload(project.RepositoryUrl, requestedRef),
            idempotencyKey,
            cancellationToken);

        var response = new BackgroundJobResponse(
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
        return Accepted($"/api/v1/jobs/{job.Id}", response);
    }

    [HttpGet("snapshots")]
    [EndpointSummary("List repository snapshots belonging to the signed-in user")]
    [ProducesResponseType<IReadOnlyList<RepositorySnapshotResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RepositorySnapshotResponse>>> ListSnapshots(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        if (await projectService.FindForOwnerAsync(ownerId, projectId, cancellationToken) is null)
        {
            return NotFound();
        }

        var snapshots = await snapshotRepository.ListForProjectOwnerAsync(ownerId, projectId, cancellationToken);
        return Ok(snapshots.Select(Map).ToArray());
    }

    [HttpGet("snapshots/{snapshotId:guid}/files")]
    [EndpointSummary("List files from an owned repository snapshot")]
    [ProducesResponseType<RepositoryFilePageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepositoryFilePageResponse>> ListFiles(
        Guid projectId,
        Guid snapshotId,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var snapshot = await snapshotRepository.FindForProjectOwnerAsync(
            ownerId,
            projectId,
            snapshotId,
            cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        var offset = (page - 1) * pageSize;
        var files = await snapshotRepository.ListFilesAsync(snapshotId, offset, pageSize, cancellationToken);
        return Ok(new RepositoryFilePageResponse(
            snapshotId,
            page,
            pageSize,
            snapshot.FileCount,
            files.Select(file => new RepositoryFileResponse(
                file.RelativePath,
                file.DetectedLanguage,
                file.SizeBytes,
                file.Sha256,
                file.IsBinary,
                file.ContentStorageKey is not null,
                file.SkipReason)).ToArray()));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var subject = User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static RepositorySnapshotResponse Map(RepositorySnapshot snapshot) => new(
        snapshot.Id,
        snapshot.ProjectId,
        snapshot.RepositoryUrl,
        snapshot.CommitSha,
        snapshot.ResolvedRef,
        snapshot.DefaultBranch,
        snapshot.FileCount,
        snapshot.TextFileCount,
        snapshot.BinaryFileCount,
        snapshot.SkippedFileCount,
        snapshot.ImportedAtUtc);
}
