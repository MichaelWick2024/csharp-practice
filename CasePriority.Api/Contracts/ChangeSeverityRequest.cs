using System.ComponentModel.DataAnnotations;

namespace CasePriority.Api.Contracts;

/// <summary>
/// Body for the change-severity endpoint. `[ApiController]` evaluates the range
/// rule and returns a 400 validation Problem Details before the action runs.
/// </summary>
public sealed class ChangeSeverityRequest
{
    [Range(1, 5)]
    public int Severity { get; init; }
}
