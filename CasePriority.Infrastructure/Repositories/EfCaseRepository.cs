using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CasePriority.Infrastructure.Repositories;

/// <summary>
/// EF Core-backed case store. Loads tracked entities for mutation, translates
/// EF failures back into the domain/API vocabulary: a lost update becomes
/// <see cref="CaseConcurrencyException"/> (-> 412), a unique-key violation
/// becomes <see cref="InvalidOperationException"/> (-> 409).
/// </summary>
public sealed class EfCaseRepository : ICaseRepository, IUnitOfWork
{
    private readonly CasePriorityDbContext _dbContext;

    public EfCaseRepository(CasePriorityDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SupportCase>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Cases
            .AsNoTracking()
            .OrderBy(supportCase => supportCase.CaseNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<SupportCase?> GetByCaseNumberAsync(
        string caseNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            throw new ArgumentException("Case number is required.", nameof(caseNumber));
        }

        // Tracked (no AsNoTracking): the caller may mutate it, and SaveChanges
        // must issue an UPDATE guarded by the original Version.
        return await _dbContext.Cases
            .SingleOrDefaultAsync(supportCase => supportCase.CaseNumber == caseNumber, cancellationToken);
    }

    public void Add(SupportCase supportCase)
    {
        ArgumentNullException.ThrowIfNull(supportCase);
        _dbContext.Cases.Add(supportCase);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw await TranslateConcurrencyExceptionAsync(exception, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            var caseNumber = exception.Entries
                .Select(entry => entry.Entity)
                .OfType<SupportCase>()
                .Select(supportCase => supportCase.CaseNumber)
                .FirstOrDefault() ?? "unknown";

            throw new InvalidOperationException($"Case {caseNumber} already exists.", exception);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        // 2601: unique index violation; 2627: unique/PK constraint violation.
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    private static async Task<Exception> TranslateConcurrencyExceptionAsync(
        DbUpdateConcurrencyException exception, CancellationToken cancellationToken)
    {
        var entry = exception.Entries.Single();

        if (entry.Entity is not SupportCase supportCase)
        {
            return exception;
        }

        var expectedVersion = entry.OriginalValues.GetValue<long>(nameof(SupportCase.Version));

        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
        if (databaseValues is null)
        {
            return new KeyNotFoundException(
                $"Case {supportCase.CaseNumber} was deleted before the update completed.");
        }

        var actualVersion = databaseValues.GetValue<long>(nameof(SupportCase.Version));

        return new CaseConcurrencyException(supportCase.CaseNumber, expectedVersion, actualVersion);
    }
}
