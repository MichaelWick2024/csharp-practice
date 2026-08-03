namespace CasePriorityApp;

/// <summary>
/// The calculated priority of a support case. Returning an enum instead of a
/// raw string keeps the set of valid values closed and checkable at compile
/// time (no typos like "Criticl"), the way an Apex picklist constrains values.
/// </summary>
public enum CasePriority
{
    Normal,
    High,
    Critical
}
