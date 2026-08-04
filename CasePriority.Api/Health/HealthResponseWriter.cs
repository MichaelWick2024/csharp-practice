using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CasePriority.Api.Health;

/// <summary>
/// Writes a compact JSON health report. Includes each check's name, status,
/// description, and duration — never the connection string or raw exception text
/// (health-check exceptions are not serialized here).
/// </summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMilliseconds = entry.Value.Duration.TotalMilliseconds
            })
        };

        return httpContext.Response.WriteAsJsonAsync(response, httpContext.RequestAborted);
    }
}
