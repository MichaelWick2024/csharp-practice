using CasePriority.Core.Domain;

namespace CasePriority.Api.Contracts;

/// <summary>
/// The API's view of a case, including its optimistic-concurrency `version`
/// (also surfaced as the ETag). Mapped from an immutable snapshot, so the HTTP
/// layer never holds a mutable domain object.
/// </summary>
public sealed record CaseResponse(
    string CaseNumber,
    string Subject,
    int Severity,
    bool IsOpen,
    bool IsExecutiveEscalation,
    CasePriorityLevel Priority,
    long Version)
{
    public static CaseResponse FromSnapshot(SupportCaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new CaseResponse(
            snapshot.CaseNumber,
            snapshot.Subject,
            snapshot.Severity,
            snapshot.IsOpen,
            snapshot.IsExecutiveEscalation,
            snapshot.Priority,
            snapshot.Version);
    }
}
