using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CasePriority.Api.Contracts;
using CasePriority.Core.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CasePriority.Api.Tests;

/// <summary>
/// Integration tests for the Day 6 mutation endpoints and optimistic
/// concurrency (ETag / If-Match), driven through the full HTTP pipeline.
/// </summary>
public sealed class CasesMutationApiTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public CasesMutationApiTests(InMemoryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string NewCaseNumber() => $"MUT-{Guid.NewGuid():N}"[..16];

    private async Task<CaseResponse> CreateCaseAsync(int severity = 3)
    {
        var request = new CreateCaseRequest
        {
            CaseNumber = NewCaseNumber(),
            Subject = "seed case",
            Severity = severity
        };

        var response = await _client.PostAsJsonAsync("/api/cases", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    private static HttpRequestMessage Patch(string path, string? ifMatch, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    // ---- ETags ------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsCurrentEtag()
    {
        var created = await CreateCaseAsync();

        var response = await _client.GetAsync($"/api/cases/{created.CaseNumber}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Post_ReturnsVersion1Etag()
    {
        var request = new CreateCaseRequest
        {
            CaseNumber = NewCaseNumber(),
            Subject = "seed",
            Severity = 3
        };

        var response = await _client.PostAsJsonAsync("/api/cases", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    // ---- Successful mutations --------------------------------------------

    [Fact]
    public async Task Close_WithCurrentEtag_Succeeds_AndBumpsVersion()
    {
        var created = await CreateCaseAsync();

        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/close", "\"1\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);

        var body = await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(body.IsOpen);
        Assert.Equal(2, body.Version);
    }

    [Fact]
    public async Task Reopen_AfterClose_Succeeds()
    {
        var created = await CreateCaseAsync();
        await _client.SendAsync(Patch($"/api/cases/{created.CaseNumber}/close", "\"1\""));

        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/reopen", "\"2\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.True(body!.IsOpen);
        Assert.Equal(3, body.Version);
    }

    [Fact]
    public async Task Escalate_Succeeds_AndBecomesCritical()
    {
        var created = await CreateCaseAsync(severity: 2);

        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/escalate", "\"1\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.True(body!.IsExecutiveEscalation);
        Assert.Equal(CasePriorityLevel.Critical, body.Priority);
        Assert.Equal(2, body.Version);
    }

    [Fact]
    public async Task ChangeSeverity_Succeeds()
    {
        var created = await CreateCaseAsync(severity: 2);

        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/severity", "\"1\"", new { severity = 5 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(5, body!.Severity);
        Assert.Equal(2, body.Version);
    }

    [Fact]
    public async Task ChangeSeverity_SameValue_PreservesVersionAndEtag()
    {
        // A no-op mutation must NOT bump the version/ETag: the ETag tracks actual
        // state changes, not the number of requests received.
        var created = await CreateCaseAsync(severity: 3);

        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/severity", "\"1\"", new { severity = 3 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);

        var body = await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(3, body.Severity);
        Assert.Equal(1, body.Version);
    }

    // ---- Precondition handling -------------------------------------------

    [Fact]
    public async Task Close_MissingIfMatch_Returns428()
    {
        var created = await CreateCaseAsync();

        var response = await _client.PatchAsync($"/api/cases/{created.CaseNumber}/close", content: null);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(428, problem.Status);
        Assert.Equal("Precondition required", problem.Title);
    }

    [Theory]
    [InlineData("1")]        // unquoted
    [InlineData("W/\"1\"")]  // weak validator
    [InlineData("\"abc\"")]  // non-numeric
    [InlineData("\"1\",\"2\"")] // a list
    public async Task Patch_MalformedIfMatch_Returns400(string ifMatch)
    {
        var created = await CreateCaseAsync();

        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/close", ifMatch));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stale_IfMatch_Returns412_WithVersionDetails_AndNoMutation()
    {
        var created = await CreateCaseAsync();
        // Escalate at v1 -> now v2.
        await _client.SendAsync(Patch($"/api/cases/{created.CaseNumber}/escalate", "\"1\""));

        // Change severity using the now-stale "1".
        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/severity", "\"1\"", new { severity = 5 }));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(412, problem.Status);
        Assert.Equal("Precondition failed", problem.Title);
        Assert.Equal(1L, ((JsonElement)problem.Extensions["expectedVersion"]!).GetInt64());
        Assert.Equal(2L, ((JsonElement)problem.Extensions["currentVersion"]!).GetInt64());

        // The stale request must not have changed severity.
        var current = await (await _client.GetAsync($"/api/cases/{created.CaseNumber}"))
            .Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.NotEqual(5, current!.Severity);
    }

    [Fact]
    public async Task Close_WithFreshEtag_WhenAlreadyClosed_Returns409()
    {
        var created = await CreateCaseAsync();
        await _client.SendAsync(Patch($"/api/cases/{created.CaseNumber}/close", "\"1\"")); // -> v2, closed

        // Close again with the CURRENT version "2": not stale (would be 412),
        // but the transition is invalid -> 409.
        var response = await _client.SendAsync(
            Patch($"/api/cases/{created.CaseNumber}/close", "\"2\""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Request conflict", problem!.Title);
    }

    [Fact]
    public async Task Patch_MissingCase_Returns404()
    {
        var response = await _client.SendAsync(
            Patch($"/api/cases/NOPE-{Guid.NewGuid():N}/close", "\"1\""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Concurrency ------------------------------------------------------

    [Fact]
    public async Task ConcurrentSeverityUpdates_SameEtag_ExactlyOneSucceeds()
    {
        var created = await CreateCaseAsync(severity: 3);
        var caseNumber = created.CaseNumber;

        var first = Patch($"/api/cases/{caseNumber}/severity", "\"1\"", new { severity = 4 });
        var second = Patch($"/api/cases/{caseNumber}/severity", "\"1\"", new { severity = 5 });

        var responses = await Task.WhenAll(
            _client.SendAsync(first),
            _client.SendAsync(second));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.PreconditionFailed);

        var final = await (await _client.GetAsync($"/api/cases/{caseNumber}"))
            .Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(2, final!.Version);              // exactly one write won
        Assert.Contains(final.Severity, new[] { 4, 5 });
    }

    // ---- OpenAPI ----------------------------------------------------------

    [Fact]
    public async Task OpenApi_EveryPatch_DeclaresRequiredIfMatch()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        string[] routes =
        [
            "/api/cases/{caseNumber}/close",
            "/api/cases/{caseNumber}/reopen",
            "/api/cases/{caseNumber}/escalate",
            "/api/cases/{caseNumber}/severity",
        ];

        foreach (var route in routes)
        {
            var patch = paths.GetProperty(route).GetProperty("patch");
            var parameters = patch.GetProperty("parameters").EnumerateArray();

            var ifMatch = parameters.FirstOrDefault(p =>
                p.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), "If-Match", StringComparison.OrdinalIgnoreCase) &&
                p.TryGetProperty("in", out var location) &&
                location.GetString() == "header");

            Assert.True(
                ifMatch.ValueKind == JsonValueKind.Object,
                $"{route} is missing an If-Match header parameter");
            Assert.True(
                ifMatch.GetProperty("required").GetBoolean(),
                $"{route} should declare If-Match as required");
        }
    }
}
