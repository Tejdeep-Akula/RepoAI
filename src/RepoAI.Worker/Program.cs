using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RepoAI.Application.Jobs;
using RepoAI.Infrastructure.Persistence;
using RepoAI.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);
var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
if (repositoryRoot is not null)
{
    var apiSettingsPath = Path.Combine(repositoryRoot, "src", "RepoAI.Api", "appsettings.json");
    var localApiSettingsPath = Path.Combine(repositoryRoot, "src", "RepoAI.Api", "appsettings.Local.json");
    builder.Configuration.AddJsonFile(apiSettingsPath, optional: false, reloadOnChange: false);
    builder.Configuration.AddJsonFile(localApiSettingsPath, optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}

var connectionString = builder.Configuration.GetConnectionString("RepoAI")
    ?? throw new InvalidOperationException(
        "Database connection string 'RepoAI' is missing. Configure it in the API appsettings " +
        "file for local development or as ConnectionStrings__RepoAI for the worker host.");

builder.Services.AddDbContext<RepoAIDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IBackgroundJobRepository, BackgroundJobRepository>();
builder.Services.AddScoped<IBackgroundJobHandler, DemoProgressJobHandler>();
builder.Services.AddHostedService<BackgroundJobWorker>();

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RepoAIDbContext>();
    await dbContext.Database.MigrateAsync();
}

await host.RunAsync();

static string? FindRepositoryRoot(string startPath)
{
    for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "RepoAI.slnx")))
        {
            return directory.FullName;
        }
    }

    return null;
}
