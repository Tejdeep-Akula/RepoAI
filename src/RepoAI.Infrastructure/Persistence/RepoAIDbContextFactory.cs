using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace RepoAI.Infrastructure.Persistence;

public sealed class RepoAIDbContextFactory : IDesignTimeDbContextFactory<RepoAIDbContext>
{
    public RepoAIDbContext CreateDbContext(string[] args)
    {
        var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            throw new FileNotFoundException(
                "Could not find appsettings.json. Run EF Core tools with RepoAI.Api as the startup project.",
                appSettingsPath);
        }

        using var appSettings = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        var appSettingsConnectionString = appSettings.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("RepoAI")
            .GetString();

        if (string.IsNullOrWhiteSpace(appSettingsConnectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:RepoAI is missing or empty in appsettings.json.");
        }

        var options = new DbContextOptionsBuilder<RepoAIDbContext>()
            .UseNpgsql(appSettingsConnectionString)
            .Options;

        return new RepoAIDbContext(options);
    }
}
