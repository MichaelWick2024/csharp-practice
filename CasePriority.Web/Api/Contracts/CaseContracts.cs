namespace CasePriority.Web.Api.Contracts;

// The web app knows only the API's public JSON — not the domain types.

public sealed record CaseDto(
    string CaseNumber,
    string Subject,
    int Severity,
    bool IsOpen,
    bool IsExecutiveEscalation,
    string Priority,
    long Version);

public sealed record CreateCaseDto(string CaseNumber, string Subject, int Severity);

public sealed record ChangeSeverityDto(int Severity);

/// <summary>The API's Problem Details, as the web app consumes them.</summary>
public sealed class ApiProblemDto
{
    public string? Title { get; init; }
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? TraceId { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
    public long? ExpectedVersion { get; init; }
    public long? CurrentVersion { get; init; }
}
