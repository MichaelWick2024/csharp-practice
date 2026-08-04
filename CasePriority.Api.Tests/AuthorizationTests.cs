using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CasePriority.Api.Contracts;
using CasePriority.Api.Security;
using CasePriority.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc;

namespace CasePriority.Api.Tests;

/// <summary>
/// Role-based access: Viewer can read but not write (403), CaseManager and
/// Administrator can read and write, an authenticated token with no recognized
/// role is 403, and 403 is Problem Details (never downgraded to 401).
/// </summary>
public sealed class AuthorizationTests : IClassFixture<InMemoryApiFactory>
{
    private readonly InMemoryApiFactory _factory;

    public AuthorizationTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient As(params string[] roles) => _factory.CreateAuthenticatedClient(roles);

    private static string NewCaseNumber() => $"AUTHZ-{Guid.NewGuid():N}"[..16];

    private static HttpRequestMessage Patch(string path, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    // Seed a case as a manager so read tests have something to fetch.
    private async Task<string> SeedCaseAsync()
    {
        var caseNumber = NewCaseNumber();
        var response = await As(CaseRoles.CaseManager)
            .PostAsJsonAsync("/api/cases", new { caseNumber, subject = "seed", severity = 3 });
        response.EnsureSuccessStatusCode();
        return caseNumber;
    }

    // ---- Viewer -----------------------------------------------------------

    [Fact]
    public async Task Viewer_CanGetAll()
    {
        var response = await As(CaseRoles.Viewer).GetAsync("/api/cases");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanGetOne()
    {
        var caseNumber = await SeedCaseAsync();
        var response = await As(CaseRoles.Viewer).GetAsync($"/api/cases/{caseNumber}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotCreate_Returns403()
    {
        var response = await As(CaseRoles.Viewer).PostAsJsonAsync(
            "/api/cases", new { caseNumber = NewCaseNumber(), subject = "x", severity = 3 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reopen")]
    [InlineData("escalate")]
    [InlineData("severity")]
    public async Task Viewer_CannotPatch_Returns403(string operation)
    {
        var caseNumber = await SeedCaseAsync();
        // Authorization runs before the If-Match check, so this is 403 (not 428).
        var response = await As(CaseRoles.Viewer)
            .SendAsync(Patch($"/api/cases/{caseNumber}/{operation}", "\"1\""));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- CaseManager / Administrator --------------------------------------

    [Fact]
    public async Task CaseManager_CanReadAndCreate()
    {
        var manager = As(CaseRoles.CaseManager);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/cases")).StatusCode);

        var create = await manager.PostAsJsonAsync(
            "/api/cases", new { caseNumber = NewCaseNumber(), subject = "x", severity = 3 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    [Fact]
    public async Task CaseManager_CanCloseReopenEscalateAndChangeSeverity()
    {
        var manager = As(CaseRoles.CaseManager);
        var caseNumber = NewCaseNumber();
        await manager.PostAsJsonAsync("/api/cases", new { caseNumber, subject = "x", severity = 2 });

        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(Patch($"/api/cases/{caseNumber}/close", "\"1\""))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(Patch($"/api/cases/{caseNumber}/reopen", "\"2\""))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(Patch($"/api/cases/{caseNumber}/escalate", "\"3\""))).StatusCode);

        var severity = new HttpRequestMessage(HttpMethod.Patch, $"/api/cases/{caseNumber}/severity");
        severity.Headers.TryAddWithoutValidation("If-Match", "\"4\"");
        severity.Content = JsonContent.Create(new { severity = 5 });
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(severity)).StatusCode);
    }

    [Fact]
    public async Task Administrator_CanReadAndManage()
    {
        var admin = As(CaseRoles.Administrator);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/cases")).StatusCode);
        var create = await admin.PostAsJsonAsync(
            "/api/cases", new { caseNumber = NewCaseNumber(), subject = "x", severity = 3 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    // ---- No / unrecognized role -------------------------------------------

    [Fact]
    public async Task NoRole_Returns403_NotUnauthorized_WithProblemDetailsAndTraceId()
    {
        // Authenticated (valid signed token) but with no role claim.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokens.Create(roles: []));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cases");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "support-call-002");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); // 403, not 401
        Assert.Equal("support-call-002", response.Headers.GetValues("X-Correlation-ID").First());

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(403, problem.Status);
        Assert.Equal("Forbidden", problem.Title);
        Assert.Equal("support-call-002", problem.Extensions["traceId"]?.ToString());
    }
}
