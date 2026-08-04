using System.Net;
using CasePriority.Web.Api;
using CasePriority.Web.Api.Contracts;
using CasePriority.Web.Tests.Testing;

namespace CasePriority.Web.Tests;

public sealed class CaseApiClientTests
{
    private static (CaseApiClient Client, FakeApiHandler Handler) NewClient()
    {
        var handler = new FakeApiHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test") };
        return (new CaseApiClient(http), handler);
    }

    [Fact]
    public async Task GetAll_UsesCorrectRoute()
    {
        var (client, handler) = NewClient();
        handler.RespondWith(_ => ApiResponses.CaseList(("A", 1), ("B", 1)));

        var result = await client.GetAllAsync(default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("/api/cases", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetByNumber_CapturesEtag()
    {
        var (client, handler) = NewClient();
        handler.RespondWith(_ => ApiResponses.Case("WEB-0001", version: 3));

        var result = await client.GetByNumberAsync("WEB-0001", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("\"3\"", result.EntityTag);
        Assert.Equal("/api/cases/WEB-0001", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Create_SendsCorrectJson()
    {
        var (client, handler) = NewClient();
        string? body = null;
        handler.RespondWith(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return ApiResponses.Case("WEB-0001", version: 1, status: HttpStatusCode.Created);
        });

        var result = await client.CreateAsync(new CreateCaseDto("WEB-0001", "Portal down", 3), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("\"caseNumber\":\"WEB-0001\"", body);
        Assert.Contains("\"severity\":3", body);
    }

    [Fact]
    public async Task Patch_SendsExactIfMatch()
    {
        var (client, handler) = NewClient();
        handler.RespondWith(_ => ApiResponses.Case("WEB-0001", version: 2));

        await client.CloseAsync("WEB-0001", "\"1\"", default);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("\"1\"", handler.LastRequest.Headers.GetValues("If-Match").Single());
    }

    [Fact]
    public async Task ProblemDetails_AreParsed()
    {
        var (client, handler) = NewClient();
        handler.RespondWith(_ => ApiResponses.Problem(
            HttpStatusCode.PreconditionFailed, "Precondition failed", "stale",
            extraJson: ",\"expectedVersion\":1,\"currentVersion\":2"));

        var result = await client.CloseAsync("WEB-0001", "\"1\"", default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
        Assert.Equal("Precondition failed", result.Problem!.Title);
        Assert.Equal(2, result.Problem.CurrentVersion);
    }

    [Fact]
    public async Task ValidationErrors_AreParsed()
    {
        var (client, handler) = NewClient();
        handler.RespondWith(_ => ApiResponses.ValidationProblem("Severity", "Out of range."));

        var result = await client.CreateAsync(new CreateCaseDto("X", "Y", 9), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Severity", result.Problem!.Errors!.Keys);
    }

    [Fact]
    public async Task NetworkFailure_Throws()
    {
        var handler = new FakeApiHandler();
        handler.RespondWith((_, _) => throw new HttpRequestException("boom"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test") };
        var client = new CaseApiClient(http);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAllAsync(default));
    }
}
