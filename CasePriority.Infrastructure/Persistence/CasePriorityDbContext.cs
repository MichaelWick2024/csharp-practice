using CasePriority.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace CasePriority.Infrastructure.Persistence;

/// <summary>
/// EF Core context for case persistence. Entity mappings are supplied by
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes in this assembly.
/// </summary>
public sealed class CasePriorityDbContext(DbContextOptions<CasePriorityDbContext> options)
    : DbContext(options)
{
    public DbSet<SupportCase> Cases => Set<SupportCase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CasePriorityDbContext).Assembly);
    }
}
