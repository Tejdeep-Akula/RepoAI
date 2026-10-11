using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepoAI.Application.Jobs;
using RepoAI.Domain.Entities;
using RepoAI.Infrastructure.Persistence;
using Xunit;

namespace RepoAI.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Fault injection exists only in the test host, not the shipped API.
        builder.ConfigureServices(services => services.AddControllers()
            .AddApplicationPart(typeof(FaultController).Assembly));
    }
}

[ApiController]
[Route("test-only/fault")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class FaultController : ControllerBase
{
    [HttpGet]
    public IActionResult Fail() => throw new InvalidOperationException("private-test-secret");
}

public sealed class ApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task HealthAndStatusRemainAvailable()
    {
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
        foreach (var route in new[] { "/", "/api/v1/status" })
        {
            var status = await client.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal("RepoAI", status.GetProperty("application").GetString());
        }
    }

    [Fact]
    public async Task LocalFrontendCanMakeCorsRequests()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/repositories/validate");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task CorsDoesNotAllowUnconfiguredOrigins()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/repositories/validate");
        request.Headers.Add("Origin", "http://localhost:5174");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.DoesNotContain("Access-Control-Allow-Origin", response.Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task BackgroundJobEndpointsRequireAuthentication()
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/jobs/demo", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"/api/v1/jobs/{jobId}")).StatusCode);
    }

    [Fact]
    public async Task RepositoryImportAndSnapshotEndpointsRequireAuthentication()
    {
        var projectId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/imports", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/projects/{projectId}/snapshots")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/projects/{projectId}/snapshots/{snapshotId}/files")).StatusCode);
    }

    [Fact]
    public async Task DurableJobsSupportIdempotentEnqueueProgressRetryCancellationAndRecovery()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RepoAIDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IBackgroundJobRepository>();
        var owner = new User
        {
            GoogleSubject = $"job-test-{Guid.NewGuid():N}",
            DisplayName = "Background job test"
        };
        var project = new Project
        {
            OwnerId = owner.Id,
            Name = "Background job test project",
            RepositoryUrl = "https://github.com/repoai-tests/background-jobs"
        };
        dbContext.Users.Add(owner);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        var kind = $"test.lifecycle.{Guid.NewGuid():N}";
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var service = new BackgroundJobService(repository);
        var first = await service.EnqueueAsync(project.Id, kind, new { value = "one" }, idempotencyKey, default);
        var repeated = await service.EnqueueAsync(project.Id, kind, new { value = "two" }, idempotencyKey, default);
        Assert.Equal(first.Id, repeated.Id);

        var claimed = await repository.ClaimNextAsync("test-worker-cancel", [kind], TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claimed);
        Assert.Equal(BackgroundJobStatus.Running, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.True(await repository.ReportProgressAndRenewLeaseAsync(
            claimed.Id, "test-worker-cancel", 40, TimeSpan.FromSeconds(30), default));

        var cancellation = await repository.RequestCancellationAsync(owner.Id, claimed.Id, default);
        Assert.Equal(BackgroundJobStatus.CancellationRequested, cancellation?.Status);
        Assert.False(await repository.ReportProgressAndRenewLeaseAsync(
            claimed.Id, "test-worker-cancel", 50, TimeSpan.FromSeconds(30), default));
        await repository.CancelAsync(claimed.Id, "test-worker-cancel", default);
        Assert.Equal(BackgroundJobStatus.Cancelled, claimed.Status);

        var recoveryKey = Guid.NewGuid().ToString("N");
        var recoveryJob = await service.EnqueueAsync(project.Id, kind, new { value = "restart" }, recoveryKey, default);
        var crashedWorkerClaim = await repository.ClaimNextAsync(
            "test-worker-crashed",
            [kind],
            TimeSpan.FromMilliseconds(-1),
            default);
        Assert.NotNull(crashedWorkerClaim);
        Assert.Equal(recoveryJob.Id, crashedWorkerClaim.Id);
        await repository.RecoverExpiredLeasesAsync(default);
        Assert.Equal(BackgroundJobStatus.RetryWaiting, crashedWorkerClaim.Status);

        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var retryClaim = await repository.ClaimNextAsync(
            "test-worker-retry",
            [kind],
            TimeSpan.FromSeconds(30),
            default);
        Assert.NotNull(retryClaim);
        Assert.Equal(2, retryClaim.AttemptCount);
        await repository.FailAsync(
            retryClaim.Id,
            "test-worker-retry",
            "test_failure",
            "Test failure detail",
            TimeSpan.Zero,
            default);
        Assert.Equal(BackgroundJobStatus.RetryWaiting, retryClaim.Status);
        Assert.Equal("Test failure detail", retryClaim.FailureMessage);

        var finalClaim = await repository.ClaimNextAsync(
            "test-worker-final",
            [kind],
            TimeSpan.FromSeconds(30),
            default);
        Assert.NotNull(finalClaim);
        Assert.Equal(3, finalClaim.AttemptCount);
        Assert.Null(finalClaim.FailureMessage);
        await repository.CompleteAsync(finalClaim.Id, "test-worker-final", default);
        Assert.Equal(BackgroundJobStatus.Succeeded, finalClaim.Status);
        Assert.Equal(100, finalClaim.ProgressPercentage);

        dbContext.BackgroundJobs.RemoveRange(
            await dbContext.BackgroundJobs.Where(job => job.ProjectId == project.Id).ToListAsync());
        dbContext.Projects.Remove(project);
        dbContext.Users.Remove(owner);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task SwaggerAndOpenApiDescribeSuccessAndValidationErrors()
    {
        Assert.Contains("swagger-ui", await client.GetStringAsync("/swagger/index.html"));
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var bearerScheme = document.GetProperty("components")
            .GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearerScheme.GetProperty("type").GetString());
        Assert.Equal("bearer", bearerScheme.GetProperty("scheme").GetString());
        var operation = document.GetProperty("paths")
            .GetProperty("/api/v1/repositories/validate").GetProperty("post");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(operation.TryGetProperty("requestBody", out _));

        var jobPaths = document.GetProperty("paths");
        Assert.True(jobPaths.TryGetProperty("/api/v1/projects/{projectId}/jobs/demo", out _));
        var jobStatus = jobPaths.GetProperty("/api/v1/jobs/{jobId}");
        Assert.True(jobStatus.TryGetProperty("get", out _));
        Assert.True(jobStatus.TryGetProperty("delete", out _));
        Assert.True(jobPaths.TryGetProperty("/api/v1/projects/{projectId}/imports", out _));
        Assert.True(jobPaths.TryGetProperty("/api/v1/projects/{projectId}/snapshots", out _));
        Assert.True(jobPaths.TryGetProperty("/api/v1/projects/{projectId}/snapshots/{snapshotId}/files", out _));

        var protectedOperation = jobPaths.GetProperty("/api/v1/projects/{projectId}").GetProperty("get");
        Assert.Contains(protectedOperation.GetProperty("security").EnumerateArray(), requirement =>
            requirement.TryGetProperty("Bearer", out _));
        var publicSignIn = jobPaths.GetProperty("/api/v1/auth/google").GetProperty("post");
        Assert.False(publicSignIn.TryGetProperty("security", out _));
    }

    [Fact]
    public async Task ValidUrlReturnsNormalizedContractWithoutClaimingReachability()
    {
        var response = await client.PostAsJsonAsync("/api/v1/repositories/validate",
            new { repositoryUrl = "https://GITHUB.com/dotnet/aspnetcore" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://github.com/dotnet/aspnetcore", body.GetProperty("repositoryUrl").GetString());
        Assert.False(body.GetProperty("reachabilityChecked").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("http://github.com/org/repo")]
    [InlineData("https://user:secret@github.com/org/repo")]
    [InlineData("https://github.com/org/repo?token=secret")]
    [InlineData("https://github.com/org/repo#fragment")]
    [InlineData("https://github.com:8443/org/repo")]
    [InlineData("https://github.com/")]
    public async Task InvalidUrlReturnsFieldErrors(string repositoryUrl)
    {
        var response = await client.PostAsJsonAsync("/api/v1/repositories/validate", new { repositoryUrl });
        var problem = await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("RepositoryUrl", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"repositoryUrl\":null}")]
    [InlineData("{\"repositoryUrl\":123}")]
    [InlineData("{broken-json")]
    [InlineData("")]
    public async Task MissingOrMalformedBodyReturnsProblemDetails(string body)
    {
        var response = await client.PostAsync("/api/v1/repositories/validate",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var problem = await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task OversizedFieldReturnsValidationError()
    {
        var response = await client.PostAsJsonAsync("/api/v1/repositories/validate",
            new { repositoryUrl = "https://example.com/" + new string('x', 2048) });
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task UnsupportedMediaReturnsProblemDetails()
    {
        var response = await client.PostAsync("/api/v1/repositories/validate", new StringContent("hello"));
        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
    }

    [Fact]
    public async Task UnknownRouteAndWrongMethodReturnProblemDetails()
    {
        await AssertProblem(await client.GetAsync("/missing"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await client.GetAsync("/api/v1/repositories/validate"),
            HttpStatusCode.MethodNotAllowed, "method_not_allowed");
    }

    [Fact]
    public async Task ExceptionsReturnSafeProblemDetails()
    {
        var response = await client.GetAsync("/test-only/fault");
        var problem = await AssertProblem(response, HttpStatusCode.InternalServerError, "internal_error");
        Assert.DoesNotContain("private-test-secret", problem.GetRawText());
        Assert.False(problem.TryGetProperty("exception", out _));
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task DocumentationIsNotExposedInProduction(string route)
    {
        const string clientIdVariable = "Authentication__Google__ClientId";
        const string signingKeyVariable = "Authentication__Jwt__SigningKey";
        var originalClientId = Environment.GetEnvironmentVariable(clientIdVariable);
        var originalSigningKey = Environment.GetEnvironmentVariable(signingKeyVariable);

        try
        {
            Environment.SetEnvironmentVariable(clientIdVariable, "repoai-tests.example.invalid");
            Environment.SetEnvironmentVariable(signingKeyVariable, "test-only-signing-key-with-at-least-32-bytes");
            using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
            using var productionClient = production.CreateClient();
            await AssertProblem(await productionClient.GetAsync(route), HttpStatusCode.NotFound, "not_found");
        }
        finally
        {
            Environment.SetEnvironmentVariable(clientIdVariable, originalClientId);
            Environment.SetEnvironmentVariable(signingKeyVariable, originalSigningKey);
        }
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.True(problem.TryGetProperty("instance", out _));
        return problem;
    }
}
