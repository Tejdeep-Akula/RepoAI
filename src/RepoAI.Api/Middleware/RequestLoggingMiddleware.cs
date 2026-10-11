using System.Diagnostics;

namespace RepoAI.Api.Middleware;

public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "HTTP {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms (TraceId: {TraceId})",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                Activity.Current?.Id ?? context.TraceIdentifier);
        }
    }
}
