using System.Net;

namespace CasePriority.Api.Tests;

/// <summary>Health and (development) OpenAPI endpoints stay reachable without a token.</summary>
public sealed class AnonymousEndpointsTests : IClassFixture<InMemoryApiFactory>
{
    private readonly InMemoryApiFactory _factory;

    public AnonymousEndpointsTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_WorksWithoutToken()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WorksWithoutToken()
    {
        // Anonymous: must not be an auth challenge (a 503 from the in-memory host
        // without a database is fine — the point is no 401/403).
        var response = await _factory.CreateClient().GetAsync("/health/ready");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_WorksWithoutToken()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
