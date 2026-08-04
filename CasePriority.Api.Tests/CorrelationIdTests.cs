using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace CasePriority.Api.Tests;

/// <summary>
/// Verifies CorrelationIdMiddleware through the real pipeline: a valid supplied
/// ID is echoed; a missing or invalid one is safely replaced (never rejected);
/// the same ID appears on the response header and in error Problem Details.
/// </summary>
public sealed class CorrelationIdTests : IClassFixture<InMemoryApiFactory>
{
    private const string Header = "X-Correlation-ID";
    private readonly HttpClient _client;

    public CorrelationIdTests(InMemoryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> SendGetAll(params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cases");
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
        return await _client.SendAsync(request);
    }

    private static string? CorrelationOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(Header, out var values) ? values.FirstOrDefault() : null;

    [Fact]
    public async Task ValidSuppliedId_IsEchoed()
    {
        var response = await SendGetAll((Header, "interview-demo-001"));
        Assert.Equal("interview-demo-001", CorrelationOf(response));
    }

    [Fact]
    public async Task MissingId_IsGenerated()
    {
        var response = await SendGetAll();
        var id = CorrelationOf(response);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(32, id!.Length); // Guid "N" format
        Assert.All(id, c => Assert.True(Uri.IsHexDigit(c)));
    }

    [Fact]
    public async Task MultipleValues_AreReplaced()
    {
        var response = await SendGetAll((Header, "aaa"), (Header, "bbb"));
        var id = CorrelationOf(response);

        Assert.NotEqual("aaa", id);
        Assert.NotEqual("bbb", id);
        Assert.Equal(32, id!.Length);
    }

    [Fact]
    public async Task TooLongValue_IsReplaced()
    {
        var tooLong = new string('a', 65); // MaxLength is 64
        var response = await SendGetAll((Header, tooLong));

        Assert.NotEqual(tooLong, CorrelationOf(response));
        Assert.Equal(32, CorrelationOf(response)!.Length);
    }

    [Fact]
    public async Task InvalidCharacters_AreReplaced()
    {
        var response = await SendGetAll((Header, "bad id!"));
        Assert.NotEqual("bad id!", CorrelationOf(response));
    }

    [Fact]
    public async Task UnicodeValue_IsReplaced()
    {
        // 'é' is a Unicode letter but not ASCII — must be rejected/replaced.
        var response = await SendGetAll((Header, "café-123"));
        Assert.NotEqual("café-123", CorrelationOf(response));
        Assert.Equal(32, CorrelationOf(response)!.Length);
    }

    [Fact]
    public async Task ErrorProblemDetails_TraceId_MatchesHeader()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/cases/MISSING-{Guid.NewGuid():N}");
        request.Headers.TryAddWithoutValidation(Header, "support-ticket-123");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("support-ticket-123", CorrelationOf(response));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("support-ticket-123", problem.Extensions["traceId"]?.ToString());
    }

    [Fact]
    public async Task SeparateRequests_GetSeparateGeneratedIds()
    {
        var first = CorrelationOf(await SendGetAll());
        var second = CorrelationOf(await SendGetAll());

        Assert.NotEqual(first, second);
    }
}
