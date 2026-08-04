using System.Collections.Concurrent;
using CasePriority.Core.Domain;

namespace CasePriority.Core.Repositories;

/// <summary>
/// In-memory case storage keyed by case number. Uses a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> so it is safe to register as
/// a singleton and share across concurrent web requests. Case numbers are
/// treated case-insensitively (ordinal-ignore-case comparer).
/// </summary>
public class InMemoryCaseRepository : ICaseRepository
{
    private readonly ConcurrentDictionary<string, SupportCase> _cases =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(SupportCase supportCase)
    {
        ArgumentNullException.ThrowIfNull(supportCase);

        // TryAdd is atomic: even if two requests race to add the same case
        // number, exactly one succeeds and the other is rejected.
        if (!_cases.TryAdd(supportCase.CaseNumber, supportCase))
        {
            throw new InvalidOperationException(
                $"Case {supportCase.CaseNumber} already exists.");
        }
    }

    public SupportCase? GetByCaseNumber(string caseNumber)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            throw new ArgumentException("Case number is required.", nameof(caseNumber));
        }

        return _cases.TryGetValue(caseNumber, out SupportCase? supportCase)
            ? supportCase
            : null;
    }

    public IReadOnlyList<SupportCase> GetAll()
    {
        // A fresh list, so callers can't alter the repository's collection and
        // later additions don't appear in an earlier snapshot.
        return _cases.Values.ToList();
    }
}
