using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Infrastructure.Persistence;
using RepoAI.Infrastructure.Repositories;
using RepoAI.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.Configure<SimpleConsoleFormatterOptions>(options => options.IncludeScopes = true);
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
builder.Services.AddScoped<IBackgroundJobHandler, RepositoryImportJobHandler>();
builder.Services.AddScoped<IRepositoryImportService, GitHubRepositoryImportService>();
builder.Services.AddScoped<IRepositoryContentStore, LocalRepositoryContentStore>();
builder.Services.Configure<RepositoryImportOptions>(builder.Configuration.GetSection("RepositoryImport"));
builder.Services.PostConfigure<RepositoryImportOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.StorageRoot))
    {
        options.StorageRoot = Path.Combine(
            repositoryRoot ?? AppContext.BaseDirectory,
            "data",
            "repository-content");
    }
});
builder.Services.AddHttpClient(GitHubRepositoryImportService.HttpClientName, client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RepoAI/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    });
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
