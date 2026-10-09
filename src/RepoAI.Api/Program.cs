using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RepoAI.Api.Authentication;
using RepoAI.Api.Errors;
using RepoAI.Application.Authentication;
using RepoAI.Application.Jobs;
using RepoAI.Application.Projects;
using RepoAI.Infrastructure.Authentication;
using RepoAI.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
}

var databaseConfiguration = builder.Environment.IsDevelopment()
    ? new ConfigurationBuilder()
        .SetBasePath(builder.Environment.ContentRootPath)
        .AddJsonFile("appsettings.json", optional: false)
        .Build()
    : builder.Configuration;

builder.Services.AddDbContext<RepoAIDbContext>(options =>
{
    var connectionString = databaseConfiguration.GetConnectionString("RepoAI")
        ?? throw new InvalidOperationException(
            "Database connection string 'RepoAI' is not configured. Set ConnectionStrings:RepoAI " +
            "in appsettings.json or the active application configuration.");

    options.UseNpgsql(connectionString);
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var jwtSigningKey = builder.Configuration["Authentication:Jwt:SigningKey"];
var hasValidJwtSigningKey = !string.IsNullOrWhiteSpace(jwtSigningKey)
    && Encoding.UTF8.GetByteCount(jwtSigningKey) >= 32;

if ((!hasValidJwtSigningKey || string.IsNullOrWhiteSpace(googleClientId))
    && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Authentication is not configured. Set the Google client ID and a JWT signing key " +
        "containing at least 32 UTF-8 bytes.");
}

// Keep local Development runnable before Google credentials are configured, but
// fail closed: this ephemeral key cannot issue tokens or survive an API restart.
var jwtSigningKeyBytes = hasValidJwtSigningKey
    ? Encoding.UTF8.GetBytes(jwtSigningKey!)
    : RandomNumberGenerator.GetBytes(32);
var jwtIssuer = builder.Configuration["Authentication:Jwt:Issuer"] ?? "RepoAI";
var jwtAudience = builder.Configuration["Authentication:Jwt:Audience"] ?? "RepoAI.Api";
var signingKey = new SymmetricSecurityKey(jwtSigningKeyBytes);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name"
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
builder.Services.AddScoped<IUserAccountRepository, UserAccountRepository>();
builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
builder.Services.AddScoped<GoogleSignInService>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<IBackgroundJobRepository, BackgroundJobRepository>();
builder.Services.AddScoped<BackgroundJobService>();

builder.Services.AddHealthChecks();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("LocalFrontend", policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}
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
            "Repository-to-video API with Google sign-in, owner-scoped projects, status, " +
            "and stateless repository URL syntax validation. Validation does not fetch, " +
            "import, or verify the existence of a repository.";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RepoAIDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalFrontend");
}

app.UseAuthentication();
app.UseAuthorization();

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
