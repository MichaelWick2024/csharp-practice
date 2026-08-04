using CasePriority.Core.Domain;

namespace CasePriorityApp.Tests;

public class SupportCaseVersioningTests
{
    private static SupportCase NewCase(
        int severity = 3,
        bool isOpen = true,
        bool isExecutiveEscalation = false) =>
        new SupportCase("0001", "Test subject", severity, isOpen, isExecutiveEscalation);

    // ---- Version & snapshot basics ---------------------------------------

    [Fact]
    public void Version_StartsAtOne()
    {
        Assert.Equal(1, NewCase().Version);
    }

    [Fact]
    public void ToSnapshot_ContainsCurrentValues()
    {
        var c = new SupportCase("0007", "Login broken", severity: 4);

        var snap = c.ToSnapshot();

        Assert.Equal("0007", snap.CaseNumber);
        Assert.Equal("Login broken", snap.Subject);
        Assert.Equal(4, snap.Severity);
        Assert.True(snap.IsOpen);
        Assert.False(snap.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.High, snap.Priority);
        Assert.Equal(1, snap.Version);
    }

    // ---- Successful mutations bump the version ---------------------------

    [Fact]
    public void Close_Versioned_IncrementsVersionAndReturnsSnapshot()
    {
        var c = NewCase();
        var snap = c.Close(expectedVersion: 1);

        Assert.False(snap.IsOpen);
        Assert.Equal(2, snap.Version);
        Assert.Equal(2, c.Version);
    }

    [Fact]
    public void Reopen_Versioned_IncrementsVersion()
    {
        var c = NewCase(isOpen: false);
        var snap = c.Reopen(expectedVersion: 1);

        Assert.True(snap.IsOpen);
        Assert.Equal(2, snap.Version);
    }

    [Fact]
    public void Escalate_Versioned_IncrementsVersion()
    {
        var c = NewCase(severity: 2);
        var snap = c.Escalate(expectedVersion: 1);

        Assert.True(snap.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.Critical, snap.Priority);
        Assert.Equal(2, snap.Version);
    }

    [Fact]
    public void ChangeSeverity_Versioned_IncrementsVersion()
    {
        var c = NewCase(severity: 2);
        var snap = c.ChangeSeverity(5, expectedVersion: 1);

        Assert.Equal(5, snap.Severity);
        Assert.Equal(2, snap.Version);
    }

    // ---- No-ops and failures do NOT bump the version ---------------------

    [Fact]
    public void Escalate_Repeated_DoesNotIncrementVersion()
    {
        var c = NewCase(severity: 2);
        c.Escalate(expectedVersion: 1);          // -> version 2
        var snap = c.Escalate(expectedVersion: 2); // already escalated, no-op

        Assert.True(snap.IsExecutiveEscalation);
        Assert.Equal(2, snap.Version); // unchanged
    }

    [Fact]
    public void ChangeSeverity_SameValue_DoesNotIncrementVersion()
    {
        var c = NewCase(severity: 3);
        var snap = c.ChangeSeverity(3, expectedVersion: 1); // no change

        Assert.Equal(3, snap.Severity);
        Assert.Equal(1, snap.Version); // unchanged
    }

    [Fact]
    public void FailedTransition_DoesNotIncrementVersion()
    {
        var c = NewCase(isOpen: false); // already closed
        Assert.Throws<InvalidOperationException>(() => c.Close(expectedVersion: 1));

        Assert.Equal(1, c.Version);
    }

    [Fact]
    public void InvalidSeverity_Versioned_DoesNotIncrementVersion()
    {
        var c = NewCase(severity: 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.ChangeSeverity(9, expectedVersion: 1));

        Assert.Equal(3, c.Severity);
        Assert.Equal(1, c.Version);
    }

    // ---- Optimistic concurrency ------------------------------------------

    [Fact]
    public void StaleVersion_ThrowsConcurrencyException()
    {
        var c = NewCase();
        Assert.Throws<CaseConcurrencyException>(() => c.Close(expectedVersion: 2));
    }

    [Fact]
    public void ConcurrencyException_ExposesExpectedAndActualVersions()
    {
        var c = NewCase();
        c.Close(expectedVersion: 1); // -> version 2

        var ex = Assert.Throws<CaseConcurrencyException>(
            () => c.Escalate(expectedVersion: 1)); // stale

        Assert.Equal("0001", ex.CaseNumber);
        Assert.Equal(1, ex.ExpectedVersion);
        Assert.Equal(2, ex.ActualVersion);
    }

    [Fact]
    public void StaleMutation_LeavesStateUnchanged()
    {
        var c = NewCase();
        Assert.Throws<CaseConcurrencyException>(() => c.Close(expectedVersion: 2));

        Assert.True(c.IsOpen);      // not closed
        Assert.Equal(1, c.Version); // not bumped
    }

    [Fact]
    public void Close_ConcurrentSameVersion_ExactlyOneSucceeds()
    {
        var supportCase = NewCase();
        var version = supportCase.Version;

        var successes = 0;
        var concurrencyFailures = 0;

        Parallel.For(0, 2, _ =>
        {
            try
            {
                supportCase.Close(version);
                Interlocked.Increment(ref successes);
            }
            catch (CaseConcurrencyException)
            {
                Interlocked.Increment(ref concurrencyFailures);
            }
        });

        Assert.Equal(1, successes);
        Assert.Equal(1, concurrencyFailures);

        var snapshot = supportCase.ToSnapshot();
        Assert.False(snapshot.IsOpen);
        Assert.Equal(2, snapshot.Version);
    }
}
