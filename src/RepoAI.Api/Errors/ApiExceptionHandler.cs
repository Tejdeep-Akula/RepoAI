using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RepoAI.Api.Errors;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled API exception for {Method} {Path} (TraceId: {TraceId})",
            context.Request.Method,
            context.Request.Path,
            System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "Please retry. If the problem persists, provide the traceId when reporting it."
            }
        });

        return true;
    }
}
