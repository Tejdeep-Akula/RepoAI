var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    application = "RepoAI",
    status = "Project foundation ready"
}));

// Process liveness only; database and model readiness checks belong to later phases.
app.MapHealthChecks("/health");

app.Run();
