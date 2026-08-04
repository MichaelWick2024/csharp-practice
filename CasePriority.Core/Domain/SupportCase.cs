using System.Threading;

namespace CasePriority.Core.Domain;

/// <summary>
/// A support case. State is encapsulated, and every mutation runs inside a
/// dedicated per-case lock together with an optimistic-concurrency version
/// check, so two callers cannot both update the same version (lost update).
/// Reads that must be consistent go through <see cref="ToSnapshot"/>.
/// </summary>
public class SupportCase
{
    // Length invariants shared by the domain, the API DTO, and the EF mapping,
    // so every caller (HTTP, console, jobs, tests) is held to what the database
    // can store.
    public const int MaxCaseNumberLength = 20;
    public const int MaxSubjectLength = 200;

    // One lock per case. Mutations and consistent reads take it; the "…Unsafe"
    // helpers assume the caller already holds it (not memory-unsafe — lock-unsafe).
    private readonly Lock _stateLock = new();

    // Version 1 is the initial state created by the constructor. It increments
    // only when a mutation actually changes the case's representation.
    private long _version = 1;

    // Identity never changes after construction — get-only, set once in the ctor.
    public string CaseNumber { get; }

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
        // (non-blank identity/subject, severity 1-5) up front. Only the
        // invariants encoded here are guaranteed.
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            throw new ArgumentException("Case number is required.", nameof(caseNumber));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        if (caseNumber.Length > MaxCaseNumberLength)
        {
            throw new ArgumentException(
                $"Case number cannot exceed {MaxCaseNumberLength} characters.", nameof(caseNumber));
        }

        if (subject.Length > MaxSubjectLength)
        {
            throw new ArgumentException(
                $"Subject cannot exceed {MaxSubjectLength} characters.", nameof(subject));
        }

        ValidateSeverity(severity);

        CaseNumber = caseNumber;
        Subject = subject;
        Severity = severity;
        IsOpen = isOpen;
        IsExecutiveEscalation = isExecutiveEscalation;
    }

    /// <summary>The current optimistic-concurrency version (starts at 1).</summary>
    public long Version
    {
        get
        {
            lock (_stateLock)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Calculated priority. Locks so it reads a consistent (severity, escalation)
    /// pair even while another thread mutates. Escalation wins over raw severity.
    /// </summary>
    public CasePriorityLevel Priority
    {
        get
        {
            lock (_stateLock)
            {
                return CalculatePriorityUnsafe();
            }
        }
    }

    private CasePriorityLevel CalculatePriorityUnsafe()
    {
        if (IsExecutiveEscalation)
        {
            return CasePriorityLevel.Critical;
        }

        if (Severity >= 5)
        {
            return CasePriorityLevel.Critical;
        }

        if (Severity >= 3)
        {
            return CasePriorityLevel.High;
        }

        return CasePriorityLevel.Normal;
    }

    /// <summary>An immutable, internally-consistent view of this case.</summary>
    public SupportCaseSnapshot ToSnapshot()
    {
        lock (_stateLock)
        {
            return CreateSnapshotUnsafe();
        }
    }

    private SupportCaseSnapshot CreateSnapshotUnsafe()
    {
        return new SupportCaseSnapshot(
            CaseNumber,
            Subject,
            Severity,
            IsOpen,
            IsExecutiveEscalation,
            CalculatePriorityUnsafe(),
            _version);
    }

    // The single critical section shared by every mutation: check the expected
    // version, apply the domain rule, bump the version only if the representation
    // changed, and capture the snapshot — all while holding the lock.
    private SupportCaseSnapshot Mutate(long? expectedVersion, Func<bool> applyMutation)
    {
        lock (_stateLock)
        {
            if (expectedVersion.HasValue && expectedVersion.Value != _version)
            {
                throw new CaseConcurrencyException(
                    CaseNumber, expectedVersion.Value, _version);
            }

            var stateChanged = applyMutation();

            if (stateChanged)
            {
                _version++;
            }

            return CreateSnapshotUnsafe();
        }
    }

    // ---- Mutations: unversioned (console/direct) + version-aware (web) --------

    public void Close()
    {
        _ = Mutate(expectedVersion: null, ApplyClose);
    }

    public SupportCaseSnapshot Close(long expectedVersion)
    {
        return Mutate(expectedVersion, ApplyClose);
    }

    private bool ApplyClose()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"Case {CaseNumber} is already closed.");
        }

        IsOpen = false;
        return true;
    }

    public void Reopen()
    {
        _ = Mutate(expectedVersion: null, ApplyReopen);
    }

    public SupportCaseSnapshot Reopen(long expectedVersion)
    {
        return Mutate(expectedVersion, ApplyReopen);
    }

    private bool ApplyReopen()
    {
        if (IsOpen)
        {
            throw new InvalidOperationException($"Case {CaseNumber} is already open.");
        }

        IsOpen = true;
        return true;
    }

    public void Escalate()
    {
        _ = Mutate(expectedVersion: null, ApplyEscalation);
    }

    public SupportCaseSnapshot Escalate(long expectedVersion)
    {
        return Mutate(expectedVersion, ApplyEscalation);
    }

    private bool ApplyEscalation()
    {
        if (IsExecutiveEscalation)
        {
            return false; // idempotent — no representation change, no version bump
        }

        IsExecutiveEscalation = true;
        return true;
    }

    public void ChangeSeverity(int severity)
    {
        _ = Mutate(expectedVersion: null, () => ApplySeverityChange(severity));
    }

    public SupportCaseSnapshot ChangeSeverity(int severity, long expectedVersion)
    {
        return Mutate(expectedVersion, () => ApplySeverityChange(severity));
    }

    private bool ApplySeverityChange(int severity)
    {
        ValidateSeverity(severity);

        if (Severity == severity)
        {
            return false; // no-op — no version bump
        }

        Severity = severity;
        return true;
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
