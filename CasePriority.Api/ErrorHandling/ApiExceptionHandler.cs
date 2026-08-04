using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CasePriority.Api.ErrorHandling;

/// <summary>
/// Central mapping from domain/service exceptions to HTTP Problem Details, so
/// controllers don't each catch-and-translate. The layering pays off here:
/// KeyNotFoundException -> 404, InvalidOperationException -> 409,
/// ArgumentException -> 400, anything else -> 500.
/// </summary>
public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            KeyNotFoundException =>
                (StatusCodes.Status404NotFound, "Case not found"),

            InvalidOperationException =>
                (StatusCodes.Status409Conflict, "Request conflict"),

            ArgumentException =>
                (StatusCodes.Status400BadRequest, "Invalid request"),

            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        await problemDetailsService.WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = statusCode,
                    Title = title,
                    Detail = statusCode >= StatusCodes.Status500InternalServerError
                        ? "An unexpected error occurred."
                        : exception.Message,
                    Instance = httpContext.Request.Path
                }
            });

        return true;
    }
}
