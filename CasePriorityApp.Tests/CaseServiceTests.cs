using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CasePriorityApp.Tests;

public class CaseServiceTests
{
    // The in-memory repository is both the store and the unit of work.
    private static CaseService NewService(out InMemoryCaseRepository repository)
    {
        repository = new InMemoryCaseRepository();
        return new CaseService(repository, repository, NullLogger<CaseService>.Instance);
    }

    // ---- Construction / DI ------------------------------------------------

    [Fact]
    public void Constructor_NullRepository_Throws()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Throws<ArgumentNullException>(
            () => new CaseService(null!, repository, NullLogger<CaseService>.Instance));
    }

    [Fact]
    public void Constructor_NullUnitOfWork_Throws()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Throws<ArgumentNullException>(
            () => new CaseService(repository, null!, NullLogger<CaseService>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Throws<ArgumentNullException>(() => new CaseService(repository, repository, null!));
    }

    [Fact]
    public async Task CreateCase_ReturnsVersion1Snapshot_AndSaves()
    {
        var repository = new RecordingCaseRepository();
        var service = new CaseService(repository, repository, NullLogger<CaseService>.Instance);

        var created = await service.CreateCaseAsync("0001", "Login broken", severity: 3);

        Assert.IsType<SupportCaseSnapshot>(created);
        Assert.Equal("0001", created.CaseNumber);
        Assert.Equal(1, created.Version);
        Assert.Equal("0001", repository.AddedCase!.CaseNumber);
        Assert.Equal(1, repository.SaveCount); // committed through the unit of work
    }

    [Fact]
    public async Task CreateCase_Duplicate_PropagatesRepositoryException()
    {
        var service = NewService(out _);
        await service.CreateCaseAsync("0001", "First", severity: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateCaseAsync("0001", "Second", severity: 4));
    }

    [Fact]
    public async Task CreateCase_InvalidSeverity_PropagatesDomainValidation()
    {
        var service = NewService(out _);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CreateCaseAsync("0001", "Bad", severity: 9));
    }

    // ---- Reads return snapshots ------------------------------------------

    [Fact]
    public async Task GetCaseByNumber_ReturnsSnapshot()
    {
        var service = NewService(out _);
        await service.CreateCaseAsync("0001", "A", severity: 3);

        var snap = await service.GetCaseByNumberAsync("0001");

        Assert.IsType<SupportCaseSnapshot>(snap);
        Assert.Equal("0001", snap.CaseNumber);
        Assert.Equal(1, snap.Version);
    }

    [Fact]
    public async Task GetCaseByNumber_Missing_ThrowsKeyNotFound()
    {
        var service = NewService(out _);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetCaseByNumberAsync("9999"));
    }

    [Fact]
    public async Task GetAllCases_ReturnsSnapshots()
    {
        var service = NewService(out _);
        await service.CreateCaseAsync("0001", "A", severity: 3);
        await service.CreateCaseAsync("0002", "B", severity: 1);

        var all = await service.GetAllCasesAsync();

        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.IsType<SupportCaseSnapshot>(s));
    }

    [Fact]
    public async Task GetOpenCasesBySeverity_ExcludesClosed_AndSorts()
    {
        var service = NewService(out _);
        await service.CreateCaseAsync("0001", "Open mid", severity: 3);
        await service.CreateCaseAsync("0002", "Closed", severity: 5);
        await service.CreateCaseAsync("0003", "Open high", severity: 5);
        var closed = await service.GetCaseByNumberAsync("0002");
        await service.CloseCaseAsync("0002", closed.Version);

        var order = (await service.GetOpenCasesBySeverityAsync())
            .Select(s => s.CaseNumber).ToArray();

        Assert.Equal(new[] { "0003", "0001" }, order); // 5 then 3; closed 0002 excluded
    }

    [Fact]
    public async Task GetOpenCasesBySeverity_EqualSeverity_TieBreaksByCaseNumber()
    {
        var service = NewService(out _);
        await service.CreateCaseAsync("0003", "Third", severity: 3);
        await service.CreateCaseAsync("0001", "First", severity: 3);
        await service.CreateCaseAsync("0002", "Second", severity: 3);

        var order = (await service.GetOpenCasesBySeverityAsync())
            .Select(s => s.CaseNumber).ToArray();

        Assert.Equal(new[] { "0001", "0002", "0003" }, order);
    }

    // ---- Version-aware mutations -----------------------------------------

    [Fact]
    public async Task CloseCase_Delegates_AndReturnsNewSnapshot()
    {
        var service = NewService(out var repository);
        var created = await service.CreateCaseAsync("0001", "A", severity: 3);

        var snap = await service.CloseCaseAsync("0001", created.Version);

        Assert.False(snap.IsOpen);
        Assert.Equal(2, snap.Version);
        Assert.False((await repository.GetByCaseNumberAsync("0001"))!.IsOpen);
    }

    [Fact]
    public async Task ReopenCase_Delegates()
    {
        var service = NewService(out _);
        var created = await service.CreateCaseAsync("0001", "A", severity: 3);
        var closed = await service.CloseCaseAsync("0001", created.Version);

        var snap = await service.ReopenCaseAsync("0001", closed.Version);

        Assert.True(snap.IsOpen);
        Assert.Equal(3, snap.Version);
    }

    [Fact]
    public async Task EscalateCase_Delegates()
    {
        var service = NewService(out _);
        var created = await service.CreateCaseAsync("0001", "A", severity: 2);

        var snap = await service.EscalateCaseAsync("0001", created.Version);

        Assert.True(snap.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.Critical, snap.Priority);
        Assert.Equal(2, snap.Version);
    }

    [Fact]
    public async Task ChangeCaseSeverity_Delegates()
    {
        var service = NewService(out _);
        var created = await service.CreateCaseAsync("0001", "A", severity: 2);

        var snap = await service.ChangeCaseSeverityAsync("0001", 5, created.Version);

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
    public async Task Operations_OnMissingCase_ThrowKeyNotFound(string operation)
    {
        var service = NewService(out _);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => operation switch
        {
            "close" => service.CloseCaseAsync("9999", 1),
            "reopen" => service.ReopenCaseAsync("9999", 1),
            "escalate" => service.EscalateCaseAsync("9999", 1),
            _ => service.ChangeCaseSeverityAsync("9999", 3, 1),
        });
    }

    [Fact]
    public async Task StaleVersion_PropagatesConcurrencyException()
    {
        var service = NewService(out _);
        var created = await service.CreateCaseAsync("0001", "A", severity: 3);
        await service.CloseCaseAsync("0001", created.Version); // -> version 2

        await Assert.ThrowsAsync<CaseConcurrencyException>(
            () => service.EscalateCaseAsync("0001", created.Version)); // stale v1
    }

    [Fact]
    public async Task CloseCase_AlreadyClosed_PropagatesInvalidOperation()
    {
        var service = NewService(out _);
        var created = await service.CreateCaseAsync("0001", "A", severity: 3);
        var closed = await service.CloseCaseAsync("0001", created.Version); // v2, now closed

        // Current version, but the transition itself is invalid -> 409 (not 412).
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CloseCaseAsync("0001", closed.Version));
    }

    [Fact]
    public async Task ChangeCaseSeverity_Invalid_LeavesStateAndVersionUnchanged()
    {
        var service = NewService(out var repository);
        var created = await service.CreateCaseAsync("0001", "A", severity: 3);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ChangeCaseSeverityAsync("0001", 9, created.Version));

        var stored = (await repository.GetByCaseNumberAsync("0001"))!;
        Assert.Equal(3, stored.Severity);
        Assert.Equal(1, stored.Version);
    }

    // ---- Cancellation -----------------------------------------------------

    [Fact]
    public async Task GetAllCases_CanceledToken_Throws()
    {
        var service = NewService(out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAllCasesAsync(cts.Token));
    }

    // Records what Add received and how often changes were committed.
    private sealed class RecordingCaseRepository : ICaseRepository, IUnitOfWork
    {
        public SupportCase? AddedCase { get; private set; }
        public int SaveCount { get; private set; }

        public void Add(SupportCase supportCase) => AddedCase = supportCase;

        public Task<IReadOnlyList<SupportCase>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupportCase>>([]);

        public Task<SupportCase?> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<SupportCase?>(null);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
