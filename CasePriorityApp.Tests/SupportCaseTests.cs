using CasePriority.Core.Domain;

namespace CasePriorityApp.Tests;

public class SupportCaseTests
{
    // A small factory so each test starts from a known-good case and only
    // varies what it cares about.
    private static SupportCase NewCase(
        int severity = 3,
        bool isOpen = true,
        bool isExecutiveEscalation = false) =>
        new SupportCase("0001", "Test subject", severity, isOpen, isExecutiveEscalation);

    // ---- Construction & defaults -----------------------------------------

    [Fact]
    public void Constructor_SetsProperties_AndDefaults()
    {
        var c = new SupportCase("0007", "Login broken", severity: 4);

        Assert.Equal("0007", c.CaseNumber);
        Assert.Equal("Login broken", c.Subject);
        Assert.Equal(4, c.Severity);
        Assert.True(c.IsOpen);                 // default
        Assert.False(c.IsExecutiveEscalation); // default
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankCaseNumber_Throws(string? caseNumber)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new SupportCase(caseNumber!, "Subject", severity: 3));
        Assert.Equal("caseNumber", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankSubject_Throws(string? subject)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new SupportCase("0001", subject!, severity: 3));
        Assert.Equal("subject", ex.ParamName);
    }

    [Fact]
    public void Constructor_CaseNumberTooLong_Throws()
    {
        var tooLong = new string('X', SupportCase.MaxCaseNumberLength + 1);
        var ex = Assert.Throws<ArgumentException>(
            () => new SupportCase(tooLong, "Subject", severity: 3));
        Assert.Equal("caseNumber", ex.ParamName);
    }

    [Fact]
    public void Constructor_SubjectTooLong_Throws()
    {
        var tooLong = new string('X', SupportCase.MaxSubjectLength + 1);
        var ex = Assert.Throws<ArgumentException>(
            () => new SupportCase("0001", tooLong, severity: 3));
        Assert.Equal("subject", ex.ParamName);
    }

    [Fact]
    public void Constructor_MaxLengths_AreAllowed()
    {
        var caseNumber = new string('X', SupportCase.MaxCaseNumberLength);
        var subject = new string('Y', SupportCase.MaxSubjectLength);
        var c = new SupportCase(caseNumber, subject, severity: 3);
        Assert.Equal(caseNumber, c.CaseNumber);
        Assert.Equal(subject, c.Subject);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Constructor_SeverityOutOfRange_Throws(int severity)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SupportCase("0001", "Subject", severity));
        Assert.Equal("severity", ex.ParamName);
    }

    // ---- Priority thresholds ---------------------------------------------

    [Theory]
    [InlineData(1, CasePriorityLevel.Normal)]
    [InlineData(2, CasePriorityLevel.Normal)]
    [InlineData(3, CasePriorityLevel.High)]
    [InlineData(4, CasePriorityLevel.High)]
    [InlineData(5, CasePriorityLevel.Critical)]
    public void Priority_FollowsSeverityThresholds(int severity, CasePriorityLevel expected)
    {
        Assert.Equal(expected, NewCase(severity: severity).Priority);
    }

    [Fact]
    public void Priority_ExecutiveEscalation_OverridesLowSeverity()
    {
        var c = NewCase(severity: 1, isExecutiveEscalation: true);
        Assert.Equal(CasePriorityLevel.Critical, c.Priority);
    }

    // ---- ChangeSeverity ---------------------------------------------------

    [Fact]
    public void ChangeSeverity_Valid_UpdatesSeverityAndPriority()
    {
        var c = NewCase(severity: 2);       // Normal
        c.ChangeSeverity(5);

        Assert.Equal(5, c.Severity);
        Assert.Equal(CasePriorityLevel.Critical, c.Priority);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ChangeSeverity_Invalid_Throws_WithCorrectParamName(int severity)
    {
        var c = NewCase(severity: 3);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => c.ChangeSeverity(severity));
        Assert.Equal("severity", ex.ParamName); // regression guard for the PR #2 fix
    }

    [Fact]
    public void ChangeSeverity_Invalid_LeavesPreviousStateIntact()
    {
        var c = NewCase(severity: 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.ChangeSeverity(9));

        Assert.Equal(3, c.Severity);              // unchanged
        Assert.Equal(CasePriorityLevel.High, c.Priority);
    }

    // ---- Close / Reopen ---------------------------------------------------

    [Fact]
    public void Close_Then_Reopen_TogglesIsOpen()
    {
        var c = NewCase();          // open
        c.Close();
        Assert.False(c.IsOpen);

        c.Reopen();
        Assert.True(c.IsOpen);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_Throws()
    {
        var c = NewCase(isOpen: false);
        Assert.Throws<InvalidOperationException>(() => c.Close());
    }

    [Fact]
    public void Reopen_WhenAlreadyOpen_Throws()
    {
        var c = NewCase(isOpen: true);
        Assert.Throws<InvalidOperationException>(() => c.Reopen());
    }

    [Fact]
    public void Close_WhenAlreadyClosed_LeavesStateIntact()
    {
        var c = NewCase(isOpen: false);
        Assert.Throws<InvalidOperationException>(() => c.Close());
        Assert.False(c.IsOpen); // still closed, no partial change
    }

    // ---- Escalate ---------------------------------------------------------

    [Fact]
    public void Escalate_SetsFlag_AndMakesPriorityCritical()
    {
        var c = NewCase(severity: 2);       // Normal
        c.Escalate();

        Assert.True(c.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.Critical, c.Priority);
    }

    [Fact]
    public void Escalate_IsIdempotent()
    {
        var c = NewCase(severity: 2);
        c.Escalate();
        c.Escalate();   // repeated call must not throw

        Assert.True(c.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.Critical, c.Priority);
    }
}
