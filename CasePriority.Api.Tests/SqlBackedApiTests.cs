using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CasePriority.Api.Contracts;
using CasePriority.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace CasePriority.Api.Tests;

/// <summary>
/// Full API-to-SQL end-to-end tests: the real app with the real EF Core / SQL
/// Server stack (no in-memory swap). Skipped when no connection string is
/// present; executed in CI. Proves persistence survives an app "restart"
/// (a fresh host over the same database) and that the database enforces
/// optimistic concurrency across the HTTP boundary.
/// </summary>
public sealed class SqlBackedApiTests
{
    private const string SkipReason =
        "No SQL Server connection string (ConnectionStrings__CasePriority); runs in CI only.";

    private static bool Available =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__CasePriority"));

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string NewCaseNumber() => $"E2E-{Guid.NewGuid():N}"[..16];

    [SkippableFact]
    public async Task Post_ThenRestart_ThenGet_PersistsAcrossHosts()
    {
        Skip.IfNot(Available, SkipReason);
        var caseNumber = NewCaseNumber();

        // Host 1 creates the case, then is disposed — the "restart".
        await using (var host1 = NewSqlHost())
        {
            var client1 = host1.CreateAuthenticatedClient();
            var create = await client1.PostAsJsonAsync(
                "/api/cases", new { caseNumber, subject = "persist across restart", severity = 3 });

            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            Assert.Equal("\"1\"", create.Headers.ETag?.Tag);
        }

        // Host 2 is a fresh application over the same database.
        await using var host2 = NewSqlHost();
        var get = await host2.CreateAuthenticatedClient().GetAsync($"/api/cases/{caseNumber}");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(caseNumber, body!.CaseNumber);
        Assert.Equal(1, body.Version);
    }

    [SkippableFact]
    public async Task Close_ThenRestart_ReflectsVersion2()
    {
        Skip.IfNot(Available, SkipReason);
        var caseNumber = NewCaseNumber();

        await using (var host1 = NewSqlHost())
        {
            var client = host1.CreateAuthenticatedClient();
            await client.PostAsJsonAsync("/api/cases", new { caseNumber, subject = "x", severity = 3 });

            var close = await client.SendAsync(Patch($"/api/cases/{caseNumber}/close", "\"1\""));
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
            Assert.Equal("\"2\"", close.Headers.ETag?.Tag);
        }

        await using var host2 = NewSqlHost();
        var body = await (await host2.CreateAuthenticatedClient().GetAsync($"/api/cases/{caseNumber}"))
            .Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        Assert.False(body!.IsOpen);
        Assert.Equal(2, body.Version);
    }

    [SkippableFact]
    public async Task CompetingPatch_SameEtag_ExactlyOneSucceeds_AgainstSql()
    {
        Skip.IfNot(Available, SkipReason);
        var caseNumber = NewCaseNumber();

        await using var host = NewSqlHost();
        var client = host.CreateAuthenticatedClient();
        await client.PostAsJsonAsync("/api/cases", new { caseNumber, subject = "x", severity = 3 });

        var first = Patch($"/api/cases/{caseNumber}/severity", "\"1\"", new { severity = 4 });
        var second = Patch($"/api/cases/{caseNumber}/severity", "\"1\"", new { severity = 5 });

        var responses = await Task.WhenAll(client.SendAsync(first), client.SendAsync(second));

        // The database concurrency token, not the in-process lock, decides this:
        // the two requests use different DbContexts and different entity instances.
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.PreconditionFailed);

        var final = await (await client.GetAsync($"/api/cases/{caseNumber}"))
            .Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(2, final!.Version);
        Assert.Contains(final.Severity, new[] { 4, 5 });
    }

    private static WebApplicationFactory<Program> NewSqlHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(TestJwtTokens.UseTestAuthentication));

    private static HttpRequestMessage Patch(string path, string ifMatch, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }
}
