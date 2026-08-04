namespace CasePriority.Core.Domain;

/// <summary>
/// A support case. State is encapsulated: identity is immutable, and every
/// other change goes through a method that keeps the object valid, rather than
/// letting outside code assign fields directly.
/// </summary>
public class SupportCase
{
    // Identity never changes after construction — get-only, set once in the ctor.
    public string CaseNumber { get; }

    // Encapsulated: readable everywhere, but only THIS class can change them,
    // and only through the validated methods below (private set).
    public string Subject { get; private set; }
    public int Severity { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsExecutiveEscalation { get; private set; }

    public SupportCase(
        string caseNumber,
        string subject,
        int severity,
        bool isOpen = true,
        bool isExecutiveEscalation = false)
    {
        // Constructor validation: enforce the invariants we choose to encode
        // (non-blank identity/subject, severity 1-5) up front, so those specific
        // rules can't be violated later. Only the invariants encoded here are
        // guaranteed — e.g. a closed-yet-escalated case is still permitted.
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            throw new ArgumentException("Case number is required.", nameof(caseNumber));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        ValidateSeverity(severity);

        CaseNumber = caseNumber;
        Subject = subject;
        Severity = severity;
        IsOpen = isOpen;
        IsExecutiveEscalation = isExecutiveEscalation;
    }

    /// <summary>
    /// Calculated priority. A computed property (not a method) because it is
    /// cheap, takes no arguments, has no side effects, and just describes the
    /// case. Escalation wins over raw severity.
    /// </summary>
    public CasePriority Priority
    {
        get
        {
            if (IsExecutiveEscalation)
            {
                return CasePriority.Critical;
            }

            if (Severity >= 5)
            {
                return CasePriority.Critical;
            }

            if (Severity >= 3)
            {
                return CasePriority.High;
            }

            return CasePriority.Normal;
        }
    }

    // ---- State-changing methods ------------------------------------------
    // These preserve the object's validation rules and invariants. (Note the
    // differences: Close/Reopen guard a transition; ChangeSeverity re-validates
    // an invariant; Escalate has no guard and is idempotent.)

    public void Escalate()
    {
        IsExecutiveEscalation = true;
    }

    public void ChangeSeverity(int severity)
    {
        ValidateSeverity(severity);
        Severity = severity;
    }

    public void Close()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"Case {CaseNumber} is already closed.");
        }

        IsOpen = false;
    }

    public void Reopen()
    {
        if (IsOpen)
        {
            throw new InvalidOperationException($"Case {CaseNumber} is already open.");
        }

        IsOpen = true;
    }

    // Shared validation so the ctor and ChangeSeverity enforce the same rule.
    private static void ValidateSeverity(int severity)
    {
        if (severity is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity), severity, "Severity must be between 1 and 5.");
        }
    }
}
