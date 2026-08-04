using CasePriority.Core.Repositories;
using CasePriority.Infrastructure.Persistence;
using CasePriority.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasePriority.Api.Tests;

/// <summary>
/// Boots the real API but replaces EF Core / SQL Server with a single shared
/// <see cref="InMemoryCaseRepository"/>, so the HTTP-pipeline tests run fast and
/// database-free while still exercising routing, binding, controllers, error
/// handling, ETags, and concurrency responses. Real SQL behavior is covered by
/// the Infrastructure tests and the SQL-backed E2E tests.
/// </summary>
public sealed class InMemoryApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A dummy connection string so Program.cs's fail-fast check passes; the
        // EF registrations are removed below and never resolved.
        builder.UseSetting(
            "ConnectionStrings:CasePriority",
            "Server=(unused);Database=Test;Encrypt=False;");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CasePriorityDbContext>>();
            services.RemoveAll<CasePriorityDbContext>();
            services.RemoveAll<EfCaseRepository>();
            services.RemoveAll<ICaseRepository>();
            services.RemoveAll<IUnitOfWork>();

            // The SAME instance must back both interfaces, or the repository and
            // unit of work would operate on different stores.
            services.AddSingleton<InMemoryCaseRepository>();
            services.AddSingleton<ICaseRepository>(sp => sp.GetRequiredService<InMemoryCaseRepository>());
            services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<InMemoryCaseRepository>());
        });
    }
}
