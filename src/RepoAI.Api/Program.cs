using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddControllers().AddJsonOptions(options =>
    options.AllowInputFormatterExceptionMessages = false);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["code"] = context.ProblemDetails switch
        {
            ValidationProblemDetails => "validation_failed",
            { Status: 400 } => "bad_request",
            { Status: 404 } => "not_found",
            { Status: 405 } => "method_not_allowed",
            { Status: 415 } => "unsupported_media_type",
            { Status: >= 500 } => "internal_error",
            _ => "request_failed"
        };
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "RepoAI API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Repository-to-video API. Phase 2 exposes status and stateless URL validation. " +
            "Validation does not fetch, import, or verify the existence of a repository.";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Both the UI and schema stay private to local development until access control exists.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "RepoAI API v1");
        options.DocumentTitle = "RepoAI API — Swagger";
    });
}

app.MapControllers();
// Process liveness only; database and model readiness checks belong to later phases.
app.MapHealthChecks("/health");

app.Run();

// Allows integration tests to boot the real application pipeline.
public partial class Program;
