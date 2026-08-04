using System.Collections.Concurrent;
using CasePriority.Core.Domain;

namespace CasePriority.Core.Repositories;

/// <summary>
/// In-memory case storage keyed by case number. Backs the console app and the
/// unit/API tests. Implements both <see cref="ICaseRepository"/> and
/// <see cref="IUnitOfWork"/> — its writes are immediate (they mutate the stored
/// objects), so <see cref="SaveChangesAsync"/> is a no-op. Uses a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> so it is safe to share.
/// </summary>
public sealed class InMemoryCaseRepository : ICaseRepository, IUnitOfWork
{
    private readonly ConcurrentDictionary<string, SupportCase> _cases =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(SupportCase supportCase)
    {
        ArgumentNullException.ThrowIfNull(supportCase);

        // TryAdd is atomic: even if two callers race to add the same case
        // number, exactly one succeeds and the other is rejected.
        if (!_cases.TryAdd(supportCase.CaseNumber, supportCase))
        {
            throw new InvalidOperationException(
                $"Case {supportCase.CaseNumber} already exists.");
        }
    }

    public Task<IReadOnlyList<SupportCase>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SupportCase> snapshot = _cases.Values.ToList();
        return Task.FromResult(snapshot);
    }

    public Task<SupportCase?> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetByCaseNumberInternal(caseNumber));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // In-memory changes already happened on the stored objects.
        return Task.CompletedTask;
    }

    private SupportCase? GetByCaseNumberInternal(string caseNumber)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            throw new ArgumentException("Case number is required.", nameof(caseNumber));
        }

        return _cases.TryGetValue(caseNumber, out SupportCase? supportCase)
            ? supportCase
            : null;
    }
}
