namespace CasePriority.Core.Domain;

/// <summary>
/// Thrown when a versioned mutation supplies an expected version that no longer
/// matches the case's current version (a lost-update / stale-write attempt).
/// Distinct from <see cref="InvalidOperationException"/> so the API can map a
/// stale version to 412 Precondition Failed and an invalid state transition to
/// 409 Conflict.
/// </summary>
public sealed class CaseConcurrencyException : Exception
{
    public CaseConcurrencyException(
        string caseNumber,
        long expectedVersion,
        long actualVersion)
        : base(
            $"Case {caseNumber} is version {actualVersion}, " +
            $"but version {expectedVersion} was supplied.")
    {
        CaseNumber = caseNumber;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string CaseNumber { get; }

    public long ExpectedVersion { get; }

    public long ActualVersion { get; }
}
