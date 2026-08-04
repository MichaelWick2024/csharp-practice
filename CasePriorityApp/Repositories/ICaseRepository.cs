namespace CasePriorityApp.Repositories;

/// <summary>
/// Persistence contract for support cases. The service depends on this
/// abstraction, so storage can change from in-memory to a database later
/// without rewriting the service.
/// </summary>
public interface ICaseRepository
{
    /// <summary>
    /// Returns a snapshot of all stored cases. Structural changes to the
    /// returned collection do not affect the repository.
    /// </summary>
    IReadOnlyList<SupportCase> GetAll();

    /// <summary>The matching case, or <c>null</c> if none exists.</summary>
    SupportCase? GetByCaseNumber(string caseNumber);

    /// <summary>Stores a new case. Implementations reject duplicates.</summary>
    void Add(SupportCase supportCase);
}
