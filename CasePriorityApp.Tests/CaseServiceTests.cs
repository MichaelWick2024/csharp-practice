using CasePriorityApp;
using CasePriorityApp.Repositories;
using CasePriorityApp.Services;

namespace CasePriorityApp.Tests;

public class CaseServiceTests
{
    private static CaseService NewService(out InMemoryCaseRepository repository)
    {
        repository = new InMemoryCaseRepository();
        return new CaseService(repository);
    }

    // ---- Construction / DI ------------------------------------------------

    [Fact]
    public void Constructor_NullRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CaseService(null!));
    }

    [Fact]
    public void CreateCase_PassesNewCaseToRepository()
    {
        // A hand-written test double proves the service works with ANY
        // ICaseRepository, not just the in-memory one.
        var repository = new RecordingCaseRepository();
        var service = new CaseService(repository);

        var created = service.CreateCase("0008", "Printer unavailable", severity: 2);

        Assert.Same(created, repository.AddedCase);
    }

    // ---- CreateCase -------------------------------------------------------

    [Fact]
    public void CreateCase_AddsAndReturnsCase()
    {
        var service = NewService(out var repository);

        var created = service.CreateCase("0001", "Login broken", severity: 3);

        Assert.Equal("0001", created.CaseNumber);
        Assert.Same(created, repository.GetByCaseNumber("0001"));
    }

    [Fact]
    public void CreateCase_Duplicate_PropagatesRepositoryException()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "First", severity: 3);

        Assert.Throws<InvalidOperationException>(
            () => service.CreateCase("0001", "Second", severity: 4));
    }

    [Fact]
    public void CreateCase_InvalidSeverity_PropagatesDomainValidation()
    {
        var service = NewService(out _);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.CreateCase("0001", "Bad", severity: 9));
    }

    // ---- Queries ----------------------------------------------------------

    [Fact]
    public void GetAllCases_ReturnsRepositoryCases()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "A", severity: 3);
        service.CreateCase("0002", "B", severity: 1);

        Assert.Equal(2, service.GetAllCases().Count);
    }

    [Fact]
    public void GetOpenCasesBySeverity_ExcludesClosedCases()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "Open", severity: 3);
        service.CreateCase("0002", "Closed", severity: 5);
        service.CloseCase("0002");

        var open = service.GetOpenCasesBySeverity();

        Assert.Single(open);
        Assert.Equal("0001", open[0].CaseNumber);
    }

    [Fact]
    public void GetOpenCasesBySeverity_SortsBySeverityDescending()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "Low", severity: 1);
        service.CreateCase("0002", "High", severity: 5);
        service.CreateCase("0003", "Mid", severity: 3);

        var order = service.GetOpenCasesBySeverity().Select(c => c.CaseNumber).ToArray();

        Assert.Equal(new[] { "0002", "0003", "0001" }, order);
    }

    [Fact]
    public void GetOpenCasesBySeverity_EqualSeverity_TieBreaksByCaseNumber()
    {
        var service = NewService(out _);
        service.CreateCase("0003", "Third", severity: 3);
        service.CreateCase("0001", "First", severity: 3);
        service.CreateCase("0002", "Second", severity: 3);

        var order = service.GetOpenCasesBySeverity().Select(c => c.CaseNumber).ToArray();

        Assert.Equal(new[] { "0001", "0002", "0003" }, order);
    }

    // ---- Operations -------------------------------------------------------

    [Fact]
    public void CloseCase_ClosesRequestedCase()
    {
        var service = NewService(out var repository);
        service.CreateCase("0001", "A", severity: 3);

        service.CloseCase("0001");

        Assert.False(repository.GetByCaseNumber("0001")!.IsOpen);
    }

    [Fact]
    public void ReopenCase_ReopensRequestedCase()
    {
        var service = NewService(out var repository);
        service.CreateCase("0001", "A", severity: 3);
        service.CloseCase("0001");

        service.ReopenCase("0001");

        Assert.True(repository.GetByCaseNumber("0001")!.IsOpen);
    }

    [Fact]
    public void EscalateCase_EscalatesRequestedCase()
    {
        var service = NewService(out var repository);
        service.CreateCase("0001", "A", severity: 2);

        service.EscalateCase("0001");

        var stored = repository.GetByCaseNumber("0001")!;
        Assert.True(stored.IsExecutiveEscalation);
        Assert.Equal(CasePriority.Critical, stored.Priority);
    }

    [Fact]
    public void ChangeCaseSeverity_UpdatesSeverityAndPriority()
    {
        var service = NewService(out var repository);
        service.CreateCase("0001", "A", severity: 2); // Normal

        service.ChangeCaseSeverity("0001", 5);

        var stored = repository.GetByCaseNumber("0001")!;
        Assert.Equal(5, stored.Severity);
        Assert.Equal(CasePriority.Critical, stored.Priority);
    }

    // ---- Missing cases & propagated errors --------------------------------

    [Theory]
    [InlineData("close")]
    [InlineData("reopen")]
    [InlineData("escalate")]
    [InlineData("severity")]
    public void Operations_OnMissingCase_ThrowKeyNotFound(string operation)
    {
        var service = NewService(out _);

        Assert.Throws<KeyNotFoundException>(() =>
        {
            switch (operation)
            {
                case "close": service.CloseCase("9999"); break;
                case "reopen": service.ReopenCase("9999"); break;
                case "escalate": service.EscalateCase("9999"); break;
                case "severity": service.ChangeCaseSeverity("9999", 3); break;
            }
        });
    }

    [Fact]
    public void CloseCase_AlreadyClosed_PropagatesInvalidOperation()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "A", severity: 3);
        service.CloseCase("0001");

        Assert.Throws<InvalidOperationException>(() => service.CloseCase("0001"));
    }

    [Fact]
    public void ChangeCaseSeverity_Invalid_LeavesPriorSeverityUnchanged()
    {
        var service = NewService(out var repository);
        service.CreateCase("0001", "A", severity: 3);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.ChangeCaseSeverity("0001", 9));

        Assert.Equal(3, repository.GetByCaseNumber("0001")!.Severity);
    }

    // A minimal in-memory-free double: records what Add received and returns
    // fixed values, so the service can be tested against the interface alone.
    private sealed class RecordingCaseRepository : ICaseRepository
    {
        public SupportCase? AddedCase { get; private set; }

        public void Add(SupportCase supportCase)
        {
            AddedCase = supportCase;
        }

        public IReadOnlyList<SupportCase> GetAll()
        {
            return [];
        }

        public SupportCase? GetByCaseNumber(string caseNumber)
        {
            return null;
        }
    }
}
