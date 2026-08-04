namespace CasePriority.Core.Domain;

/// <summary>
/// An immutable, internally-consistent view of a case at one moment, including
/// its <see cref="Version"/>. Returned by the service/API instead of the mutable
/// <see cref="SupportCase"/>, so callers can't reach in and change stored state,
/// and can't observe fields captured at different instants.
/// </summary>
public sealed record SupportCaseSnapshot(
    string CaseNumber,
    string Subject,
    int Severity,
    bool IsOpen,
    bool IsExecutiveEscalation,
    CasePriorityLevel Priority,
    long Version);
