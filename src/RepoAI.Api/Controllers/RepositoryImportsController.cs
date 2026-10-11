using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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
    IRepositorySnapshotRepository snapshotRepository,
    IRepositoryArchitectureAnalysisRepository analysisRepository) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    [HttpPost("snapshots/{snapshotId:guid}/analysis")]
    [EndpointSummary("Queue static architecture analysis for an imported snapshot")]
    [ProducesResponseType<BackgroundJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BackgroundJobResponse>> CreateArchitectureAnalysis(
        Guid projectId,
        Guid snapshotId,
        [FromHeader(Name = "Idempotency-Key")] string? requestIdempotencyKey,
        CancellationToken cancellationToken)
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
            BackgroundJobKinds.RepositoryAnalysis,
            new RepositoryAnalysisJobPayload(snapshotId),
            idempotencyKey,
            cancellationToken);
        var response = Map(job);
        return Accepted($"/api/v1/jobs/{job.Id}", response);
    }

    [HttpGet("snapshots/{snapshotId:guid}/architecture")]
    [EndpointSummary("Get the saved architecture analysis for an imported snapshot")]
    [ProducesResponseType<RepositoryArchitectureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepositoryArchitectureResponse>> GetArchitecture(
        Guid projectId,
        Guid snapshotId,
        CancellationToken cancellationToken)
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

        var analysis = await analysisRepository.FindForOwnerAsync(
            ownerId,
            projectId,
            snapshotId,
            cancellationToken);
        if (analysis is null)
        {
            return NotFound();
        }

        var report = JsonSerializer.Deserialize<RepositoryArchitectureReport>(analysis.ReportJson, JsonOptions)
            ?? throw new InvalidOperationException("The saved repository architecture report is invalid.");
        return Ok(new RepositoryArchitectureResponse(
            snapshot.Id,
            analysis.CommitSha,
            analysis.AnalyzerVersion,
            analysis.GeneratedAtUtc,
            report.Summary,
            report.LanguagesAndFormats.Select(Map).ToArray(),
            report.Components.Select(component => new RepositoryArchitectureComponentResponse(
                component.Name,
                component.Path,
                component.FileCount,
                component.LanguagesAndFormats.Select(Map).ToArray(),
                component.SourcePaths)).ToArray(),
            report.LikelyEntryPoints.Select(evidence => new RepositoryArchitectureEvidenceResponse(
                evidence.Path,
                evidence.Reason)).ToArray(),
            report.BuildAndDependencyFiles.Select(evidence => new RepositoryArchitectureEvidenceResponse(
                evidence.Path,
                evidence.Reason)).ToArray(),
            report.Limitations,
            report.Findings.Select(finding => new RepositoryArchitectureFindingResponse(
                finding.Category,
                finding.Claim,
                finding.Kind.ToString(),
                finding.Confidence.ToString(),
                finding.Method,
                finding.Evidence.Select(reference => new RepositoryArchitectureSourceReferenceResponse(
                    reference.Path,
                    reference.StartLine,
                    reference.EndLine)).ToArray())).ToArray(),
            report.Graph is null
                ? null
                : new RepositoryArchitectureGraphResponse(
                    report.Graph.Nodes.Select(node => new RepositoryArchitectureGraphNodeResponse(
                        node.Id,
                        node.Label,
                        node.Kind,
                        node.Detail,
                        node.Path)).ToArray(),
                    report.Graph.Edges.Select(edge => new RepositoryArchitectureGraphEdgeResponse(
                        edge.Id,
                        edge.SourceNodeId,
                        edge.TargetNodeId,
                        edge.RelationshipType,
                        edge.Evidence.Select(reference => new RepositoryArchitectureSourceReferenceResponse(
                            reference.Path,
                            reference.StartLine,
                            reference.EndLine)).ToArray(),
                        edge.Confidence)).ToArray(),
                    report.Graph.IsTruncated,
                    report.Graph.OmittedEdgeCount)));
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

    private static RepositoryArchitectureLanguageResponse Map(RepositoryArchitectureLanguage language) =>
        new(language.Name, language.FileCount);
}
