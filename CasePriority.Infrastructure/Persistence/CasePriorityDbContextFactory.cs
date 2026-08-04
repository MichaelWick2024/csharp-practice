using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CasePriority.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI (`dotnet ef`). It reads the
/// connection string from the ConnectionStrings__CasePriority environment
/// variable when present (so `database update` targets a real database, e.g. in
/// CI), and otherwise falls back to a non-connecting placeholder — enough to
/// scaffold `migrations add`, which never opens a connection.
/// </summary>
public sealed class CasePriorityDbContextFactory : IDesignTimeDbContextFactory<CasePriorityDbContext>
{
    public CasePriorityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__CasePriority")
            ?? "Server=localhost;Database=CasePriorityDesignTime;Trusted_Connection=False;Encrypt=False;";

        var options = new DbContextOptionsBuilder<CasePriorityDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CasePriorityDbContext(options);
    }
}
