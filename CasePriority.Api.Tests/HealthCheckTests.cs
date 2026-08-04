using System.Net;
using System.Text.Json;
using CasePriority.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CasePriority.Api.Tests;

public sealed class HealthCheckTests : IClassFixture<InMemoryApiFactory>
{
    private const string SkipReason =
        "No SQL Server connection string (ConnectionStrings__CasePriority); runs in CI only.";

    private static bool SqlAvailable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__CasePriority"));

    // A bad connection string used to prove readiness turns Unhealthy without a
    // real database — no retry, short timeout, so it fails fast.
    private const string UnreachableConnectionString =
        "Server=localhost,1;Database=x;User Id=sa;Password=UNUSED_pw_1;Encrypt=False;Connect Timeout=1;TrustServerCertificate=True";

    private readonly InMemoryApiFactory _factory;

    public HealthCheckTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    // ---- Liveness (fast, DB-free) -----------------------------------------

    [Fact]
    public async Task Live_Returns200_Healthy_WithNoDependencyChecks()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.Empty(doc.RootElement.GetProperty("checks").EnumerateArray()); // no SQL probe
    }

    [Fact]
    public async Task Live_IncludesCorrelationHeader()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    // ---- Readiness turns Unhealthy on an unreachable database -------------

    [Fact]
    public async Task Ready_UnreachableSql_Returns503_AndHidesConnectionString()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // UseSetting (host config) is read early enough for Program.cs's
            // connection-string check; ConfigureAppConfiguration would be too late.
            builder.UseSetting("ConnectionStrings:CasePriority", UnreachableConnectionString);

            builder.ConfigureTestServices(services =>
            {
                // Re-register the DbContext without retry so the probe fails fast.
                services.RemoveAll<DbContextOptions<CasePriorityDbContext>>();
                services.RemoveAll<CasePriorityDbContext>();
                services.AddDbContext<CasePriorityDbContext>(o => o.UseSqlServer(UnreachableConnectionString));
            });
        });

        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("sqlserver", body);
        Assert.Contains("Unhealthy", body);
        // Never expose the connection string / password.
        Assert.DoesNotContain("Password", body);
        Assert.DoesNotContain("Server=", body);
    }

    // ---- Readiness against real SQL Server (CI only) ----------------------

    [SkippableFact]
    public async Task Ready_Healthy_ListsSqlServer()
    {
        Skip.IfNot(SqlAvailable, SkipReason);

        await using var factory = new WebApplicationFactory<Program>();
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());

        var checks = doc.RootElement.GetProperty("checks").EnumerateArray().ToList();
        var sql = checks.Single(c => c.GetProperty("name").GetString() == "sqlserver");
        Assert.Equal("Healthy", sql.GetProperty("status").GetString());
    }
}
