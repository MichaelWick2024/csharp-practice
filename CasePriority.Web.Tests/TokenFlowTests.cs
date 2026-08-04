using System.Net;
using System.Security.Claims;
using CasePriority.Web.Api;
using CasePriority.Web.Configuration;
using CasePriority.Web.Tests.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CasePriority.Web.Tests;

public sealed class TokenFlowTests
{
    // ---- ApiAccessTokenProvider -------------------------------------------

    private static ApiAccessTokenProvider Provider() =>
        new(Options.Create(new CaseApiOptions
        {
            BaseAddress = new Uri("https://api.test"),
            ViewerToken = "viewer-token",
            CaseManagerToken = "manager-token",
            AdministratorToken = "admin-token",
        }));

    private static ClaimsPrincipal UserWith(string? role)
    {
        var claims = role is null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.Role, role)];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData("Viewer", "viewer-token")]
    [InlineData("CaseManager", "manager-token")]
    [InlineData("Administrator", "admin-token")]
    public void Provider_ReturnsRoleAppropriateToken(string role, string expected)
    {
        Assert.Equal(expected, Provider().GetToken(UserWith(role)));
    }

    [Fact]
    public void Provider_UnrecognizedRole_ThrowsSafely()
    {
        Assert.Throws<InvalidOperationException>(() => Provider().GetToken(UserWith(role: null)));
    }

    // ---- ApiBearerTokenHandler --------------------------------------------

    private sealed class StubTokenProvider(string token) : IApiAccessTokenProvider
    {
        public string GetToken(ClaimsPrincipal user) => token;
    }

    [Fact]
    public async Task Handler_AttachesBearerToken_AndPropagatesCorrelationId()
    {
        var capture = new FakeApiHandler();
        HttpRequestMessage? seen = null;
        capture.RespondWith(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpContext = new DefaultHttpContext { TraceIdentifier = "corr-123" };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var handler = new ApiBearerTokenHandler(accessor, new StubTokenProvider("the-token"))
        {
            InnerHandler = capture
        };
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/cases"), default);

        Assert.Equal("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.Equal("the-token", seen.Headers.Authorization.Parameter);
        Assert.Equal("corr-123", seen.Headers.GetValues("X-Correlation-ID").Single());
    }
}
