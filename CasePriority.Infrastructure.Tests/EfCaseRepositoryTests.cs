using CasePriority.Core.Domain;
using CasePriority.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CasePriority.Infrastructure.Tests;

/// <summary>
/// Real SQL Server integration tests for <see cref="EfCaseRepository"/>: mapping,
/// persistence across DbContext scopes, case-insensitive keys, unique-constraint
/// handling, and database-level optimistic concurrency. Skipped when no
/// connection string is present; executed in CI against a live SQL Server.
/// One test class => xUnit runs these sequentially, and every test uses a unique
/// case number, so they never collide on the shared CI database.
/// </summary>
public sealed class EfCaseRepositoryTests
{
    [SkippableFact]
    public async Task Migration_CreatesSupportCasesTable()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);

        await using var context = SqlServerSupport.NewDbContext();
        // Throws if the table/migration is missing.
        var count = await context.Cases.CountAsync();
        Assert.True(count >= 0);
    }

    [SkippableFact]
    public async Task Case_PersistsAcrossDbContextScopes_AtVersion1()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();

        await using (var context = SqlServerSupport.NewDbContext())
        {
            var repository = new EfCaseRepository(context);
            repository.Add(new SupportCase(caseNumber, "persist me", severity: 4));
            await repository.SaveChangesAsync();
        }

        await using var fresh = SqlServerSupport.NewDbContext();
        var loaded = await new EfCaseRepository(fresh).GetByCaseNumberAsync(caseNumber);

        Assert.NotNull(loaded);
        Assert.Equal("persist me", loaded!.Subject);
        Assert.Equal(4, loaded.Severity);
        Assert.True(loaded.IsOpen);
        Assert.Equal(1, loaded.Version);
    }

    [SkippableFact]
    public async Task Lookup_IsCaseInsensitive()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber().ToLowerInvariant();

        await using var context = SqlServerSupport.NewDbContext();
        var repository = new EfCaseRepository(context);
        repository.Add(new SupportCase(caseNumber, "subject", severity: 3));
        await repository.SaveChangesAsync();

        var found = await repository.GetByCaseNumberAsync(caseNumber.ToUpperInvariant());
        Assert.NotNull(found);
    }

    [SkippableFact]
    public async Task Duplicate_CaseNumber_Throws_InvalidOperation()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();

        await using (var first = SqlServerSupport.NewDbContext())
        {
            var repo = new EfCaseRepository(first);
            repo.Add(new SupportCase(caseNumber, "first", severity: 3));
            await repo.SaveChangesAsync();
        }

        await using var second = SqlServerSupport.NewDbContext();
        var repository = new EfCaseRepository(second);
        repository.Add(new SupportCase(caseNumber, "second", severity: 4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Duplicate_DifferentCasing_Throws_InvalidOperation()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();

        await using (var first = SqlServerSupport.NewDbContext())
        {
            var repo = new EfCaseRepository(first);
            repo.Add(new SupportCase(caseNumber.ToLowerInvariant(), "first", severity: 3));
            await repo.SaveChangesAsync();
        }

        await using var second = SqlServerSupport.NewDbContext();
        var repository = new EfCaseRepository(second);
        repository.Add(new SupportCase(caseNumber.ToUpperInvariant(), "second", severity: 4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Close_PersistsVersion2_InFreshContext()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();
        await Seed(caseNumber, severity: 3);

        await using (var context = SqlServerSupport.NewDbContext())
        {
            var repository = new EfCaseRepository(context);
            var supportCase = await repository.GetByCaseNumberAsync(caseNumber);
            supportCase!.Close(supportCase.Version);
            await repository.SaveChangesAsync();
        }

        await using var fresh = SqlServerSupport.NewDbContext();
        var after = await new EfCaseRepository(fresh).GetByCaseNumberAsync(caseNumber);
        Assert.False(after!.IsOpen);
        Assert.Equal(2, after.Version);
    }

    [SkippableFact]
    public async Task NoOpSeverityChange_PreservesVersion()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();
        await Seed(caseNumber, severity: 3);

        await using (var context = SqlServerSupport.NewDbContext())
        {
            var repository = new EfCaseRepository(context);
            var supportCase = await repository.GetByCaseNumberAsync(caseNumber);
            supportCase!.ChangeSeverity(3, supportCase.Version); // same value -> no change
            await repository.SaveChangesAsync();                 // no UPDATE issued
        }

        await using var fresh = SqlServerSupport.NewDbContext();
        var after = await new EfCaseRepository(fresh).GetByCaseNumberAsync(caseNumber);
        Assert.Equal(3, after!.Severity);
        Assert.Equal(1, after.Version); // unchanged
    }

    [SkippableFact]
    public async Task TwoContexts_SameVersion_ExactlyOneUpdateWins()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();
        await Seed(caseNumber, severity: 3);

        // Two independent DbContexts both load version 1.
        await using var contextA = SqlServerSupport.NewDbContext();
        await using var contextB = SqlServerSupport.NewDbContext();
        var repoA = new EfCaseRepository(contextA);
        var repoB = new EfCaseRepository(contextB);

        var caseA = await repoA.GetByCaseNumberAsync(caseNumber);
        var caseB = await repoB.GetByCaseNumberAsync(caseNumber);

        // A closes and saves first -> database version 2.
        caseA!.Close(caseA.Version);
        await repoA.SaveChangesAsync();

        // B still holds version 1; its UPDATE ... WHERE Version = 1 affects zero
        // rows -> DbUpdateConcurrencyException -> translated to CaseConcurrency.
        caseB!.Escalate(caseB.Version);
        var ex = await Assert.ThrowsAsync<CaseConcurrencyException>(() => repoB.SaveChangesAsync());

        Assert.Equal(caseNumber, ex.CaseNumber);
        Assert.Equal(1, ex.ExpectedVersion);
        Assert.Equal(2, ex.ActualVersion);

        // The database reflects only the winning update.
        await using var verify = SqlServerSupport.NewDbContext();
        var final = await new EfCaseRepository(verify).GetByCaseNumberAsync(caseNumber);
        Assert.Equal(2, final!.Version);
        Assert.False(final.IsOpen);                 // A's close won
        Assert.False(final.IsExecutiveEscalation);  // B's escalate lost
    }

    [SkippableFact]
    public async Task InvalidTransition_IssuesNoUpdate()
    {
        Skip.IfNot(SqlServerSupport.Available, SqlServerSupport.SkipReason);
        var caseNumber = SqlServerSupport.NewCaseNumber();
        await Seed(caseNumber, severity: 3);

        // Close it (version 2).
        await using (var context = SqlServerSupport.NewDbContext())
        {
            var repository = new EfCaseRepository(context);
            var supportCase = await repository.GetByCaseNumberAsync(caseNumber);
            supportCase!.Close(supportCase.Version);
            await repository.SaveChangesAsync();
        }

        // Closing again throws in the domain, before any SaveChanges/UPDATE.
        await using var second = SqlServerSupport.NewDbContext();
        var repo2 = new EfCaseRepository(second);
        var reloaded = await repo2.GetByCaseNumberAsync(caseNumber);
        Assert.Throws<InvalidOperationException>(() => reloaded!.Close(reloaded.Version));

        await using var verify = SqlServerSupport.NewDbContext();
        var after = await new EfCaseRepository(verify).GetByCaseNumberAsync(caseNumber);
        Assert.Equal(2, after!.Version); // unchanged by the failed transition
    }

    private static async Task Seed(string caseNumber, int severity)
    {
        await using var context = SqlServerSupport.NewDbContext();
        var repository = new EfCaseRepository(context);
        repository.Add(new SupportCase(caseNumber, "seed", severity));
        await repository.SaveChangesAsync();
    }
}
