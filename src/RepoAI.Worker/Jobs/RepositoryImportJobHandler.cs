using System.Text.Json;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;

namespace RepoAI.Worker.Jobs;

public sealed class RepositoryImportJobHandler(IRepositoryImportService repositoryImportService)
    : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => BackgroundJobKinds.RepositoryImport;

    public async Task ExecuteAsync(
        BackgroundJob job,
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<RepositoryImportJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new BackgroundJobFailureException("invalid_import_payload", "The repository import job payload is invalid.");

        await repositoryImportService.ImportAsync(job.ProjectId, payload, executionContext, cancellationToken);
    }
}
