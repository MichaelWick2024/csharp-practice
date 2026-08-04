using CasePriority.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CasePriority.Infrastructure.Health;

/// <summary>
/// Readiness check: can the app actually reach SQL Server? Resolves a scoped
/// <see cref="CasePriorityDbContext"/> and probes connectivity. Never surfaces
/// the connection string or raw SQL error text in its result.
/// </summary>
public sealed class SqlServerHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CasePriorityDbContext>();

            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server is reachable.")
                : HealthCheckResult.Unhealthy("SQL Server is not reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The exception is attached for internal logging, but the public
            // description stays generic — no connection string / SQL details.
            return HealthCheckResult.Unhealthy("SQL Server health check failed.", exception);
        }
    }
}
