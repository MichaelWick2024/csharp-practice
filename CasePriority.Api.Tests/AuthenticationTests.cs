using System.Net;
using System.Net.Http.Json;
using System.Text;
using CasePriority.Api.Security;
using CasePriority.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace CasePriority.Api.Tests;

/// <summary>
/// Exercises the REAL JWT bearer handler: missing/malformed/expired/wrong-key/
/// wrong-issuer/wrong-audience tokens all yield 401 with a Bearer challenge and
/// Problem Details, and no token/validation details leak.
/// </summary>
public sealed class AuthenticationTests : IClassFixture<InMemoryApiFactory>
{
    private readonly InMemoryApiFactory _factory;

    public AuthenticationTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithRawAuth(string? authorizationHeader)
    {
        var client = _factory.CreateClient();
        if (authorizationHeader is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        }
        return client;
    }

    private HttpClient ClientWithToken(string token) => ClientWithRawAuth($"Bearer {token}");

    [Fact]
    public async Task NoToken_Returns401_WithChallenge_AndProblemDetails()
    {
        var response = await _factory.CreateClient().GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(401, problem.Status);
        Assert.Equal("Unauthorized", problem.Title);
    }

    [Theory]
    [InlineData("Bearer ")]           // empty token
    [InlineData("Bearer not-a-jwt")]  // nonsensical
    public async Task MalformedToken_Returns401(string header)
    {
        var response = await ClientWithRawAuth(header).GetAsync("/api/cases");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = TestJwtTokens.Create(
            roles: [CaseRoles.Administrator], expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await ClientWithToken(token).GetAsync("/api/cases");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongSigningKey_Returns401()
    {
        var wrongKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("a-different-signing-key-also-32-bytes!!!"));
        var token = TestJwtTokens.Create(roles: [CaseRoles.Administrator], signingKey: wrongKey);

        var response = await ClientWithToken(token).GetAsync("/api/cases");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var token = TestJwtTokens.Create(roles: [CaseRoles.Administrator], issuer: "evil-issuer");
        var response = await ClientWithToken(token).GetAsync("/api/cases");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        var token = TestJwtTokens.Create(roles: [CaseRoles.Administrator], audience: "some-other-api");
        var response = await ClientWithToken(token).GetAsync("/api/cases");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_SharesCorrelationId_AndHidesTokenDetails()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cases");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "support-call-001");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("support-call-001", response.Headers.GetValues("X-Correlation-ID").First());

        var body = await response.Content.ReadAsStringAsync();
        // No validation internals (IDX error codes), key material, or token text.
        Assert.DoesNotContain("IDX", body);
        Assert.DoesNotContain("signing", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal("support-call-001", doc.RootElement.GetProperty("traceId").GetString());
    }
}
