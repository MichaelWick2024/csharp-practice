using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;

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
    public void CreateCase_ReturnsVersion1Snapshot()
    {
        var service = NewService(out var repository);

        var created = service.CreateCase("0001", "Login broken", severity: 3);

        Assert.IsType<SupportCaseSnapshot>(created);
        Assert.Equal("0001", created.CaseNumber);
        Assert.Equal(1, created.Version);
        Assert.NotNull(repository.GetByCaseNumber("0001"));
    }

    [Fact]
    public void CreateCase_PassesNewCaseToRepository()
    {
        // A hand-written test double proves the service works with ANY
        // ICaseRepository, not just the in-memory one.
        var repository = new RecordingCaseRepository();
        var service = new CaseService(repository);

        var created = service.CreateCase("0008", "Printer unavailable", severity: 2);

        Assert.NotNull(repository.AddedCase);
        Assert.Equal("0008", repository.AddedCase!.CaseNumber);
        Assert.Equal(created.CaseNumber, repository.AddedCase.CaseNumber);
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

    // ---- Reads return snapshots ------------------------------------------

    [Fact]
    public void GetCaseByNumber_ReturnsSnapshot()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "A", severity: 3);

        var snap = service.GetCaseByNumber("0001");

        Assert.IsType<SupportCaseSnapshot>(snap);
        Assert.Equal("0001", snap.CaseNumber);
        Assert.Equal(1, snap.Version);
    }

    [Fact]
    public void GetAllCases_ReturnsSnapshots()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "A", severity: 3);
        service.CreateCase("0002", "B", severity: 1);

        var all = service.GetAllCases();

        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.IsType<SupportCaseSnapshot>(s));
    }

    [Fact]
    public void GetOpenCasesBySeverity_ExcludesClosed_AndSorts()
    {
        var service = NewService(out _);
        service.CreateCase("0001", "Open mid", severity: 3);
        service.CreateCase("0002", "Closed", severity: 5);
        service.CreateCase("0003", "Open high", severity: 5);
        var closed = service.GetCaseByNumber("0002");
        service.CloseCase("0002", closed.Version);

        var order = service.GetOpenCasesBySeverity().Select(s => s.CaseNumber).ToArray();

        Assert.Equal(new[] { "0003", "0001" }, order); // 5 then 3; closed 0002 excluded
    }

    [Fact]
    public void GetOpenCasesBySeverity_EqualSeverity_TieBreaksByCaseNumber()
    {
        var service = NewService(out _);
        service.CreateCase("0003", "Third", severity: 3);
        service.CreateCase("0001", "First", severity: 3);
        service.CreateCase("0002", "Second", severity: 3);

        var order = service.GetOpenCasesBySeverity().Select(s => s.CaseNumber).ToArray();

        Assert.Equal(new[] { "0001", "0002", "0003" }, order);
    }

    // ---- Version-aware mutations -----------------------------------------

    [Fact]
    public void CloseCase_Delegates_AndReturnsNewSnapshot()
    {
        var service = NewService(out var repository);
        var created = service.CreateCase("0001", "A", severity: 3);

        var snap = service.CloseCase("0001", created.Version);

        Assert.False(snap.IsOpen);
        Assert.Equal(2, snap.Version);
        Assert.False(repository.GetByCaseNumber("0001")!.IsOpen);
    }

    [Fact]
    public void ReopenCase_Delegates()
    {
        var service = NewService(out _);
        var created = service.CreateCase("0001", "A", severity: 3);
        var closed = service.CloseCase("0001", created.Version);

        var snap = service.ReopenCase("0001", closed.Version);

        Assert.True(snap.IsOpen);
        Assert.Equal(3, snap.Version);
    }

    [Fact]
    public void EscalateCase_Delegates()
    {
        var service = NewService(out _);
        var created = service.CreateCase("0001", "A", severity: 2);

        var snap = service.EscalateCase("0001", created.Version);

        Assert.True(snap.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.Critical, snap.Priority);
        Assert.Equal(2, snap.Version);
    }

    [Fact]
    public void ChangeCaseSeverity_Delegates()
    {
        var service = NewService(out _);
        var created = service.CreateCase("0001", "A", severity: 2);

        var snap = service.ChangeCaseSeverity("0001", 5, created.Version);

        Assert.Equal(5, snap.Severity);
        Assert.Equal(CasePriorityLevel.Critical, snap.Priority);
        Assert.Equal(2, snap.Version);
    }

    // ---- Errors propagate through the service ----------------------------

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
                case "close": service.CloseCase("9999", 1); break;
                case "reopen": service.ReopenCase("9999", 1); break;
                case "escalate": service.EscalateCase("9999", 1); break;
                case "severity": service.ChangeCaseSeverity("9999", 3, 1); break;
            }
        });
    }

    [Fact]
    public void StaleVersion_PropagatesConcurrencyException()
    {
        var service = NewService(out _);
        var created = service.CreateCase("0001", "A", severity: 3);
        service.CloseCase("0001", created.Version); // -> version 2

        Assert.Throws<CaseConcurrencyException>(
            () => service.EscalateCase("0001", created.Version)); // stale v1
    }

    [Fact]
    public void CloseCase_AlreadyClosed_PropagatesInvalidOperation()
    {
        var service = NewService(out _);
        var created = service.CreateCase("0001", "A", severity: 3);
        var closed = service.CloseCase("0001", created.Version); // v2, now closed

        // Current version, but the transition itself is invalid -> 409 (not 412).
        Assert.Throws<InvalidOperationException>(
            () => service.CloseCase("0001", closed.Version));
    }

    [Fact]
    public void ChangeCaseSeverity_Invalid_LeavesStateAndVersionUnchanged()
    {
        var service = NewService(out var repository);
        var created = service.CreateCase("0001", "A", severity: 3);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.ChangeCaseSeverity("0001", 9, created.Version));

        var stored = repository.GetByCaseNumber("0001")!;
        Assert.Equal(3, stored.Severity);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public void Reads_ReturnSnapshots_NotTheStoredEntity()
    {
        // The service boundary hands back immutable snapshots, so callers can't
        // reach the stored SupportCase and mutate it directly.
        var service = NewService(out var repository);
        service.CreateCase("0001", "A", severity: 3);

        Assert.IsType<SupportCaseSnapshot>(service.GetCaseByNumber("0001"));
        Assert.Equal(1, repository.GetByCaseNumber("0001")!.Version);
        Assert.True(repository.GetByCaseNumber("0001")!.IsOpen);
    }

    // A minimal in-memory-free double: records what Add received.
    private sealed class RecordingCaseRepository : ICaseRepository
    {
        public SupportCase? AddedCase { get; private set; }

        public void Add(SupportCase supportCase) => AddedCase = supportCase;

        public IReadOnlyList<SupportCase> GetAll() => [];

        public SupportCase? GetByCaseNumber(string caseNumber) => null;
    }
}
