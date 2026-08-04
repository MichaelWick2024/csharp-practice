using CasePriority.Core.Domain;
// The CasePriority enum shares its name with the CasePriority root namespace,
// so a bare `CasePriority` would bind to the namespace here. Alias the enum.
using Priority = CasePriority.Core.Domain.CasePriority;

namespace CasePriority.Api.Contracts;

/// <summary>
/// The API's view of a case. Keeping this separate from <c>SupportCase</c> means
/// the HTTP response shape is not permanently coupled to every domain property,
/// and gives a deliberate mapping boundary: SupportCase -> CaseResponse -> JSON.
/// </summary>
public sealed record CaseResponse(
    string CaseNumber,
    string Subject,
    int Severity,
    bool IsOpen,
    bool IsExecutiveEscalation,
    Priority Priority)
{
    public static CaseResponse FromDomain(SupportCase supportCase)
    {
        ArgumentNullException.ThrowIfNull(supportCase);

        return new CaseResponse(
            supportCase.CaseNumber,
            supportCase.Subject,
            supportCase.Severity,
            supportCase.IsOpen,
            supportCase.IsExecutiveEscalation,
            supportCase.Priority);
    }
}
