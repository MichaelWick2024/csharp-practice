namespace CasePriority.Core.Repositories;

/// <summary>
/// Commits changes staged through an <see cref="ICaseRepository"/>. Split out
/// because the in-memory store saves immediately (it holds object references),
/// while EF Core doesn't hit the database until <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
