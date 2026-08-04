using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CasePriority.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

using CasePriority.Api.Tests.Security;

namespace CasePriority.Api.Tests;

/// <summary>
/// Full-pipeline integration tests: WebApplicationFactory boots the API in an
/// in-memory server so routing, model binding, DI, controllers, serialization,
/// and middleware are all exercised together. The repository is a shared
/// singleton, so tests use unique case numbers instead of asserting counts.
/// </summary>
public sealed class CasesApiTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;

    // The API serializes the priority enum as a string ("High"). Reading it back
    // needs matching options — the client's default reader expects a number.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public CasesApiTests(InMemoryApiFactory factory)
    {
        _client = factory.CreateAuthenticatedClient();
    }

    // Unique but short enough to satisfy CreateCaseRequest's [StringLength(20)]:
    // "API-" + 12 hex = 16 chars, still collision-safe across the shared repo.
    private static string NewCaseNumber() => $"API-{Guid.NewGuid():N}"[..16];

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cases = await response.Content.ReadFromJsonAsync<List<CaseResponse>>(JsonOptions);
        Assert.NotNull(cases); // not asserting empty: the singleton repo is shared
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsCreatedCase()
    {
        var caseNumber = NewCaseNumber();
        var request = new CreateCaseRequest
        {
            CaseNumber = caseNumber,
            Subject = "API-created login issue",
            Severity = 3
        };

        var createResponse = await _client.PostAsJsonAsync("/api/cases", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        Assert.Contains(
            $"/api/cases/{caseNumber}",
            createResponse.Headers.Location!.ToString());

        var getResponse = await _client.GetAsync($"/api/cases/{caseNumber}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var created = await getResponse.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(caseNumber, created.CaseNumber);
        Assert.Equal("High", created.Priority.ToString());
    }

    [Fact]
    public async Task Create_InvalidSeverity_ReturnsBadRequest()
    {
        var request = new CreateCaseRequest
        {
            CaseNumber = NewCaseNumber(),
            Subject = "Invalid severity",
            Severity = 9
        };

        var response = await _client.PostAsJsonAsync("/api/cases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Severity", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetByCaseNumber_Missing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/cases/MISSING-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Case not found", problem.Title);
    }

    [Fact]
    public async Task Create_Duplicate_ReturnsConflict()
    {
        var caseNumber = NewCaseNumber();
        var request = new CreateCaseRequest
        {
            CaseNumber = caseNumber,
            Subject = "Duplicate test",
            Severity = 2
        };

        var first = await _client.PostAsJsonAsync("/api/cases", request);
        var second = await _client.PostAsJsonAsync("/api/cases", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Prove the duplicate maps to Problem Details via the central handler.
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Request conflict", problem.Title);
    }

    [Fact]
    public async Task Create_SerializesPriorityAsString()
    {
        var request = new CreateCaseRequest
        {
            CaseNumber = NewCaseNumber(),
            Subject = "Serialization test",
            Severity = 5
        };

        var response = await _client.PostAsJsonAsync("/api/cases", request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"priority\":\"Critical\"", json);
        Assert.DoesNotContain("\"priority\":2", json);
    }

    [Fact]
    public async Task OpenApi_ContainsCasesRoute()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/cases", json);
    }
}
