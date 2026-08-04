using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;

namespace CasePriority.Core.Services;

/// <summary>
/// Coordinates application use cases over the case store. It depends on the
/// <see cref="ICaseRepository"/> abstraction (constructor injection), not on a
/// concrete storage type, so the storage mechanism can change without touching
/// this service.
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
    /// constructor, so it is not duplicated here.
    /// </summary>
    public SupportCase CreateCase(string caseNumber, string subject, int severity)
    {
        var supportCase = new SupportCase(caseNumber, subject, severity);
        _repository.Add(supportCase);
        return supportCase;
    }

    public IReadOnlyList<SupportCase> GetAllCases()
    {
        return _repository.GetAll();
    }

    /// <summary>The requested case, or <see cref="KeyNotFoundException"/> if absent.</summary>
    public SupportCase GetCaseByNumber(string caseNumber)
    {
        return GetRequiredCase(caseNumber);
    }

    /// <summary>
    /// Open cases sorted by raw severity (highest first), with case number as a
    /// stable tie-breaker. Named for severity deliberately, to avoid conflating
    /// raw severity with the computed <see cref="SupportCase.Priority"/>.
    /// </summary>
    public IReadOnlyList<SupportCase> GetOpenCasesBySeverity()
    {
        return _repository
            .GetAll()
            .Where(supportCase => supportCase.IsOpen)
            .OrderByDescending(supportCase => supportCase.Severity)
            .ThenBy(supportCase => supportCase.CaseNumber)
            .ToList();
    }

    public void CloseCase(string caseNumber)
    {
        GetRequiredCase(caseNumber).Close();
    }

    public void ReopenCase(string caseNumber)
    {
        GetRequiredCase(caseNumber).Reopen();
    }

    public void EscalateCase(string caseNumber)
    {
        GetRequiredCase(caseNumber).Escalate();
    }

    public void ChangeCaseSeverity(string caseNumber, int severity)
    {
        GetRequiredCase(caseNumber).ChangeSeverity(severity);
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
