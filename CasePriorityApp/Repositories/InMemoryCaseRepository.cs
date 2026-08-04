namespace CasePriorityApp.Repositories;

/// <summary>
/// In-memory case storage keyed by case number. A dictionary fits because
/// cases are looked up by their unique case number. Case numbers are treated
/// case-insensitively (the dictionary uses an ordinal-ignore-case comparer).
/// </summary>
public class InMemoryCaseRepository : ICaseRepository
{
    private readonly Dictionary<string, SupportCase> _cases =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(SupportCase supportCase)
    {
        ArgumentNullException.ThrowIfNull(supportCase);

        if (_cases.ContainsKey(supportCase.CaseNumber))
        {
            throw new InvalidOperationException(
                $"Case {supportCase.CaseNumber} already exists.");
        }

        _cases.Add(supportCase.CaseNumber, supportCase);
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
        // Return a new list, not the live Values view, so callers can't alter
        // the repository's internal collection through the returned reference.
        return _cases.Values.ToList();
    }
}
