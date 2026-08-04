namespace CasePriority.Api.Configuration;

/// <summary>
/// Bound from the "RequestTracing" configuration section and validated at
/// startup, so bad settings stop the app rather than surfacing per-request.
/// </summary>
public sealed class RequestTracingOptions
{
    public const string SectionName = "RequestTracing";

    public string HeaderName { get; init; } = "X-Correlation-ID";

    public int MaxLength { get; init; } = 64;
}
