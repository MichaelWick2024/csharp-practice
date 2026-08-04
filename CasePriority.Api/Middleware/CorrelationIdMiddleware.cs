using CasePriority.Api.Configuration;
using Microsoft.Extensions.Options;

namespace CasePriority.Api.Middleware;

/// <summary>
/// Ensures every request has a correlation ID: it honors a valid client-supplied
/// value, otherwise generates one. The ID is set as <see cref="HttpContext.TraceIdentifier"/>,
/// echoed in the response header, and opened as a logging scope so all downstream
/// logs and the Problem Details traceId share it. An invalid ID is replaced, never
/// rejected.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestTracingOptions _options;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        IOptions<RequestTracingOptions> options,
        ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var values = httpContext.Request.Headers[_options.HeaderName];

        // Only a single, valid supplied value is honored.
        var suppliedValue = values.Count == 1 ? values[0] : null;

        var correlationId = IsValid(suppliedValue)
            ? suppliedValue!
            : Guid.NewGuid().ToString("N");

        httpContext.TraceIdentifier = correlationId;

        // Set the header via OnStarting so it survives even when the exception
        // handler clears and rewrites the response — error responses must carry
        // the same correlation ID as their Problem Details traceId.
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[_options.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope("CorrelationId: {CorrelationId}", correlationId))
        {
            await _next(httpContext);
        }
    }

    private bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= _options.MaxLength
            && value.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
    }
}
