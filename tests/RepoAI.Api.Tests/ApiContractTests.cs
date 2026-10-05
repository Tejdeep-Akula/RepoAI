using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task SwaggerAndOpenApiDescribeSuccessAndValidationErrors()
    {
        Assert.Contains("swagger-ui", await client.GetStringAsync("/swagger/index.html"));
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var operation = document.GetProperty("paths")
            .GetProperty("/api/v1/repositories/validate").GetProperty("post");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(operation.TryGetProperty("requestBody", out _));
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
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var productionClient = production.CreateClient();
        await AssertProblem(await productionClient.GetAsync(route), HttpStatusCode.NotFound, "not_found");
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
