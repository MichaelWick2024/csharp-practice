using CasePriority.Api.Http;
using CasePriority.Core.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CasePriority.Api.ErrorHandling;

/// <summary>
/// Central mapping from domain/service exceptions to HTTP Problem Details, so
/// controllers don't each catch-and-translate:
/// PreconditionRequiredException -> 428, CaseConcurrencyException -> 412,
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
        // The client disconnected / aborted the request. Not a server error, and
        // there's no point writing a response to a gone connection.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "Request canceled for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
            return true;
        }

        var (statusCode, title) = exception switch
        {
            PreconditionRequiredException =>
                (StatusCodes.Status428PreconditionRequired, "Precondition required"),

            CaseConcurrencyException =>
                (StatusCodes.Status412PreconditionFailed, "Precondition failed"),

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

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode >= StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception.Message,
            Instance = httpContext.Request.Path
        };

        // traceId (= the correlation ID) is attached centrally via
        // AddProblemDetails' CustomizeProblemDetails, so it's consistent across
        // every Problem Details, including validation 400s.

        // Give a stale-write client the machine-readable versions it needs to
        // re-fetch and reconcile.
        if (exception is CaseConcurrencyException concurrency)
        {
            problemDetails.Extensions["caseNumber"] = concurrency.CaseNumber;
            problemDetails.Extensions["expectedVersion"] = concurrency.ExpectedVersion;
            problemDetails.Extensions["currentVersion"] = concurrency.ActualVersion;
        }

        await problemDetailsService.WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problemDetails
            });

        return true;
    }
}
