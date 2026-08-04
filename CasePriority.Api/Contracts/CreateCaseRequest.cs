using System.ComponentModel.DataAnnotations;

namespace CasePriority.Api.Contracts;

/// <summary>
/// The API contract for creating a case. Distinct from the domain object: these
/// data-annotation rules give HTTP clients a clean 400 before the action runs.
/// The domain (<c>SupportCase</c>) still enforces its own rules for every other
/// caller (console, tests, future jobs).
/// </summary>
public sealed class CreateCaseRequest
{
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string CaseNumber { get; init; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    [Range(1, 5)]
    public int Severity { get; init; }
}
