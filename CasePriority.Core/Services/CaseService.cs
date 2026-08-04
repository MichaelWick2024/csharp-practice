using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;

namespace CasePriority.Core.Services;

/// <summary>
/// Coordinates application use cases over the case store. Depends on the
/// <see cref="ICaseRepository"/> abstraction (constructor injection), not a
/// concrete storage type. Its API-facing boundary hands back immutable
/// <see cref="SupportCaseSnapshot"/> values, and every mutation requires the
/// caller's expected version (optimistic concurrency).
/// </summary>
public class CaseService
{
    private readonly ICaseRepository _repository;

    public CaseService(ICaseRepository repository)
    {
        // The service does not construct its own repository — one is supplied
        // from outside (the composition root). That is constructor injection.
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>
    /// Creates and stores a case. Validation lives in <see cref="SupportCase"/>'s
    /// constructor, so it is not duplicated here. Returns a version-1 snapshot.
    /// </summary>
    public SupportCaseSnapshot CreateCase(string caseNumber, string subject, int severity)
    {
        var supportCase = new SupportCase(caseNumber, subject, severity);
        _repository.Add(supportCase);
        return supportCase.ToSnapshot();
    }

    public IReadOnlyList<SupportCaseSnapshot> GetAllCases()
    {
        return _repository
            .GetAll()
            .Select(supportCase => supportCase.ToSnapshot())
            .ToList();
    }

    /// <summary>The requested case's snapshot, or <see cref="KeyNotFoundException"/> if absent.</summary>
    public SupportCaseSnapshot GetCaseByNumber(string caseNumber)
    {
        return GetRequiredCase(caseNumber).ToSnapshot();
    }

    /// <summary>
    /// Open cases sorted by raw severity (highest first), with case number as a
    /// stable tie-breaker. Named for severity deliberately, to avoid conflating
    /// raw severity with the computed priority.
    /// </summary>
    public IReadOnlyList<SupportCaseSnapshot> GetOpenCasesBySeverity()
    {
        return _repository
            .GetAll()
            .Select(supportCase => supportCase.ToSnapshot())
            .Where(snapshot => snapshot.IsOpen)
            .OrderByDescending(snapshot => snapshot.Severity)
            .ThenBy(snapshot => snapshot.CaseNumber)
            .ToList();
    }

    // ---- Version-aware mutations -----------------------------------------
    // Each finds the case (404 if missing) and applies the versioned domain
    // operation, which throws CaseConcurrencyException on a stale version.

    public SupportCaseSnapshot CloseCase(string caseNumber, long expectedVersion)
    {
        return GetRequiredCase(caseNumber).Close(expectedVersion);
    }

    public SupportCaseSnapshot ReopenCase(string caseNumber, long expectedVersion)
    {
        return GetRequiredCase(caseNumber).Reopen(expectedVersion);
    }

    public SupportCaseSnapshot EscalateCase(string caseNumber, long expectedVersion)
    {
        return GetRequiredCase(caseNumber).Escalate(expectedVersion);
    }

    public SupportCaseSnapshot ChangeCaseSeverity(string caseNumber, int severity, long expectedVersion)
    {
        return GetRequiredCase(caseNumber).ChangeSeverity(severity, expectedVersion);
    }

    // The repository returns null for a missing case; the service decides that
    // operating on a missing case is an error. (The domain object decides
    // whether a given state transition is itself valid.)
    private SupportCase GetRequiredCase(string caseNumber)
    {
        return _repository.GetByCaseNumber(caseNumber)
            ?? throw new KeyNotFoundException($"Case {caseNumber} was not found.");
    }
}
