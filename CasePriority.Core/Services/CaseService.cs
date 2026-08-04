using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;

namespace CasePriority.Core.Services;

/// <summary>
/// Coordinates application use cases over the case store. Depends on
/// <see cref="ICaseRepository"/> (query/stage) and <see cref="IUnitOfWork"/>
/// (commit) via constructor injection. Its API-facing boundary returns
/// immutable <see cref="SupportCaseSnapshot"/> values, and every mutation
/// requires the caller's expected version (optimistic concurrency).
/// </summary>
public class CaseService
{
    private readonly ICaseRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CaseService(ICaseRepository repository, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Creates and stores a case, then commits. Validation lives in
    /// <see cref="SupportCase"/>'s constructor. Returns a version-1 snapshot.
    /// </summary>
    public async Task<SupportCaseSnapshot> CreateCaseAsync(
        string caseNumber,
        string subject,
        int severity,
        CancellationToken cancellationToken = default)
    {
        var supportCase = new SupportCase(caseNumber, subject, severity);
        _repository.Add(supportCase);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return supportCase.ToSnapshot();
    }

    public async Task<IReadOnlyList<SupportCaseSnapshot>> GetAllCasesAsync(
        CancellationToken cancellationToken = default)
    {
        var cases = await _repository.GetAllAsync(cancellationToken);
        return cases.Select(supportCase => supportCase.ToSnapshot()).ToList();
    }

    /// <summary>The requested case's snapshot, or <see cref="KeyNotFoundException"/> if absent.</summary>
    public async Task<SupportCaseSnapshot> GetCaseByNumberAsync(
        string caseNumber,
        CancellationToken cancellationToken = default)
    {
        var supportCase = await GetRequiredCaseAsync(caseNumber, cancellationToken);
        return supportCase.ToSnapshot();
    }

    /// <summary>
    /// Open cases sorted by raw severity (highest first), with case number as a
    /// stable tie-breaker.
    /// </summary>
    public async Task<IReadOnlyList<SupportCaseSnapshot>> GetOpenCasesBySeverityAsync(
        CancellationToken cancellationToken = default)
    {
        var cases = await _repository.GetAllAsync(cancellationToken);
        return cases
            .Select(supportCase => supportCase.ToSnapshot())
            .Where(snapshot => snapshot.IsOpen)
            .OrderByDescending(snapshot => snapshot.Severity)
            .ThenBy(snapshot => snapshot.CaseNumber)
            .ToList();
    }

    // ---- Version-aware mutations -----------------------------------------
    // Load (404 if missing) -> apply the versioned domain operation (which
    // throws CaseConcurrencyException on a stale version) -> commit.

    public async Task<SupportCaseSnapshot> CloseCaseAsync(
        string caseNumber, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var supportCase = await GetRequiredCaseAsync(caseNumber, cancellationToken);
        var snapshot = supportCase.Close(expectedVersion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task<SupportCaseSnapshot> ReopenCaseAsync(
        string caseNumber, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var supportCase = await GetRequiredCaseAsync(caseNumber, cancellationToken);
        var snapshot = supportCase.Reopen(expectedVersion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task<SupportCaseSnapshot> EscalateCaseAsync(
        string caseNumber, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var supportCase = await GetRequiredCaseAsync(caseNumber, cancellationToken);
        var snapshot = supportCase.Escalate(expectedVersion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task<SupportCaseSnapshot> ChangeCaseSeverityAsync(
        string caseNumber, int severity, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var supportCase = await GetRequiredCaseAsync(caseNumber, cancellationToken);
        var snapshot = supportCase.ChangeSeverity(severity, expectedVersion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    private async Task<SupportCase> GetRequiredCaseAsync(
        string caseNumber, CancellationToken cancellationToken)
    {
        return await _repository.GetByCaseNumberAsync(caseNumber, cancellationToken)
            ?? throw new KeyNotFoundException($"Case {caseNumber} was not found.");
    }
}
