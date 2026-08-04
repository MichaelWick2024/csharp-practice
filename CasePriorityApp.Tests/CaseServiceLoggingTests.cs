using CasePriority.Core.Repositories;
using CasePriority.Core.Services;
using Microsoft.Extensions.Logging;

namespace CasePriorityApp.Tests;

public class CaseServiceLoggingTests
{
    // ---- recording logger -------------------------------------------------

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true; // capture Debug too

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), fields));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("commit failed");
    }

    private static long Version(LogEntry e) =>
        Convert.ToInt64(e.State.Single(kv => kv.Key == "Version").Value);

    // ---- tests ------------------------------------------------------------

    [Fact]
    public async Task Create_EmitsEvent1001_WithStructuredState_AndNoSubject()
    {
        var logger = new RecordingLogger<CaseService>();
        var repository = new InMemoryCaseRepository();
        var service = new CaseService(repository, repository, logger);

        await service.CreateCaseAsync("0001", "Confidential subject text", severity: 3);

        var entry = Assert.Single(logger.Entries, e => e.EventId.Id == 1001);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(entry.State, kv => kv.Key == "CaseNumber" && (string?)kv.Value == "0001");
        Assert.Contains(entry.State, kv => kv.Key == "Severity" && Convert.ToInt32(kv.Value) == 3);
        Assert.Equal(1, Version(entry));

        // The case subject must never be logged.
        Assert.DoesNotContain("Confidential subject text", entry.Message);
        Assert.DoesNotContain(entry.State, kv => (kv.Value as string) == "Confidential subject text");
    }

    [Fact]
    public async Task Close_EmitsEvent1002()
    {
        var logger = new RecordingLogger<CaseService>();
        var repository = new InMemoryCaseRepository();
        var service = new CaseService(repository, repository, logger);
        var created = await service.CreateCaseAsync("0001", "s", severity: 3);

        await service.CloseCaseAsync("0001", created.Version);

        var entry = Assert.Single(logger.Entries, e => e.EventId.Id == 1002);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(2, Version(entry));
    }

    [Fact]
    public async Task Escalate_Change_EmitsInfo_NoOp_EmitsDebug()
    {
        var logger = new RecordingLogger<CaseService>();
        var repository = new InMemoryCaseRepository();
        var service = new CaseService(repository, repository, logger);
        var created = await service.CreateCaseAsync("0001", "s", severity: 2);

        var escalated = await service.EscalateCaseAsync("0001", created.Version); // real change
        await service.EscalateCaseAsync("0001", escalated.Version);               // no-op

        var change = Assert.Single(logger.Entries, e => e.EventId.Id == 1004);
        Assert.Equal(LogLevel.Information, change.Level);

        var noOp = Assert.Single(logger.Entries, e => e.EventId.Id == 1005);
        Assert.Equal(LogLevel.Debug, noOp.Level);
    }

    [Fact]
    public async Task SeverityChange_EmitsInfo_SameValue_EmitsDebug()
    {
        var logger = new RecordingLogger<CaseService>();
        var repository = new InMemoryCaseRepository();
        var service = new CaseService(repository, repository, logger);
        var created = await service.CreateCaseAsync("0001", "s", severity: 3);

        var changed = await service.ChangeCaseSeverityAsync("0001", 5, created.Version); // real change
        await service.ChangeCaseSeverityAsync("0001", 5, changed.Version);               // same value

        var change = Assert.Single(logger.Entries, e => e.EventId.Id == 1006);
        Assert.Equal(LogLevel.Information, change.Level);
        Assert.Contains(change.State, kv => kv.Key == "Severity" && Convert.ToInt32(kv.Value) == 5);

        var noOp = Assert.Single(logger.Entries, e => e.EventId.Id == 1007);
        Assert.Equal(LogLevel.Debug, noOp.Level);
    }

    [Fact]
    public async Task Reopen_EmitsEvent1003()
    {
        var logger = new RecordingLogger<CaseService>();
        var repository = new InMemoryCaseRepository();
        var service = new CaseService(repository, repository, logger);
        var created = await service.CreateCaseAsync("0001", "s", severity: 3);
        var closed = await service.CloseCaseAsync("0001", created.Version);

        await service.ReopenCaseAsync("0001", closed.Version);

        Assert.Single(logger.Entries, e => e.EventId.Id == 1003);
    }

    [Fact]
    public async Task FailedCommit_EmitsNoSuccessEvent()
    {
        var logger = new RecordingLogger<CaseService>();
        var repository = new InMemoryCaseRepository();
        repository.Add(new CasePriority.Core.Domain.SupportCase("0001", "s", severity: 3));

        // Store works, but the commit throws — no success log should be emitted.
        var service = new CaseService(repository, new ThrowingUnitOfWork(), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseCaseAsync("0001", 1));

        Assert.DoesNotContain(logger.Entries, e => e.EventId.Id == 1002);
        // No success/no-op event of any kind when the commit failed.
        Assert.DoesNotContain(logger.Entries, e => e.EventId.Id is >= 1001 and <= 1007);
    }
}
