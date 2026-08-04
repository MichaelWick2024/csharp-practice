using CasePriority.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CasePriority.Infrastructure.Tests;

/// <summary>
/// Shared helpers for the SQL Server-backed tests. When no connection string is
/// configured (local dev on a Mac, where SQL Server can't run), the tests are
/// skipped — visibly, never falsely passed. CI supplies
/// ConnectionStrings__CasePriority and applies migrations first.
/// </summary>
internal static class SqlServerSupport
{
    public const string SkipReason =
        "No SQL Server connection string (ConnectionStrings__CasePriority); runs in CI only.";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__CasePriority");

    public static bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    public static CasePriorityDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<CasePriorityDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new CasePriorityDbContext(options);
    }

    // Unique, <= 20 chars, so parallel tests never share a case number.
    public static string NewCaseNumber() => $"IT-{Guid.NewGuid():N}"[..16];
}
