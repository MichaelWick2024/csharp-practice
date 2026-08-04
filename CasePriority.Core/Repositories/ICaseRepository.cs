using CasePriority.Core.Domain;

namespace CasePriority.Core.Repositories;

/// <summary>
/// Persistence contract for support cases. Queries are asynchronous (a real
/// database is I/O); <see cref="Add"/> stays synchronous because it only stages
/// an entity — the database write happens in <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface ICaseRepository
{
    Task<IReadOnlyList<SupportCase>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SupportCase?> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default);

    void Add(SupportCase supportCase);
}
