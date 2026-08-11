namespace Planvexa.Api.Middleware;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// Translates exceptions into RFC 9457 Problem Details responses. Domain exceptions carry their own
/// status codes; everything else is a 500 with no internal detail leaked.
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Path}", httpContext.Request.Path);

            // Surface diagnostics outside Production to speed up debugging (never in Production).
            // Domain exceptions already carry a safe, useful message (e.g. 502 from a tenant's AI
            // provider), so keep theirs rather than dumping a stack trace over it.
            if (!environment.IsProduction() && exception is not PlanvexaException)
            {
                detail = exception.ToString();
            }
        }
        else
        {
            logger.LogInformation("Request failed: {Title} ({Status}) on {Path}", title, status, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Type = $"https://httpstatuses.io/{status}",
            },
        });
    }

    private static (int Status, string Title, string Detail) Map(Exception exception) => exception switch
    {
        CrossWorkspaceAccessException ex => (StatusCodes.Status403Forbidden, "Forbidden", ex.Error.Message),
        PlanvexaException ex => (ex.StatusCode, TitleFor(ex.StatusCode), ex.Error.Message),

        // Model-binding failures from minimal APIs (missing/unparseable route, query or body values).
        // The framework already picked the right status (400) and a caller-safe message.
        BadHttpRequestException ex => (ex.StatusCode, TitleFor(ex.StatusCode), ex.Message),
        ArgumentException ex => (StatusCodes.Status400BadRequest, "Bad Request", ex.Message),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred."),
    };

    private static string TitleFor(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        429 => "Too Many Requests",
        502 => "Bad Gateway",
        _ => "Error",
    };
}
