using System.Net;
using CasePriority.Web.Tests.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CasePriority.Web.Tests;

public sealed class DevLoginTests : IClassFixture<WebTestFactory>
{
    private readonly WebTestFactory _factory;

    public DevLoginTests(WebTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LoginPage_IsAvailableInTesting()
    {
        var response = await _factory.CreateWebClient().GetAsync("/Account/DevLogin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void DevelopmentLogin_RefusesToStartOutsideDevelopmentOrTesting()
    {
        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("CaseApi:BaseAddress", "https://api.test");
            builder.UseSetting("CaseApi:ViewerToken", "v");
            builder.UseSetting("CaseApi:CaseManagerToken", "m");
            builder.UseSetting("CaseApi:AdministratorToken", "a");
            builder.UseSetting("DevelopmentLogin:AccessCode", "test-access-code");
        });

        var exception = Assert.ThrowsAny<Exception>(() => production.CreateClient());
        Assert.Contains("development-only", exception.ToString());
    }

    [Fact]
    public async Task WrongAccessCode_DoesNotAuthenticate()
    {
        var client = _factory.CreateWebClient();
        var page = await client.GetAsync("/Account/DevLogin");
        var token = WebTestFactory.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>
        {
            ["Input.AccessCode"] = "wrong-code",
            ["Input.Role"] = "Viewer",
            ["__RequestVerificationToken"] = token,
        };
        var response = await client.PostAsync("/Account/DevLogin", new FormUrlEncodedContent(form));

        // The form re-renders (200) with an error rather than redirecting.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            "__Host-CasePriority.Web",
            string.Join(";", response.Headers.TryGetValues("Set-Cookie", out var v) ? v : []));
    }

    [Fact]
    public async Task ValidLogin_CreatesSession_AndCookieHasNoApiToken()
    {
        var client = _factory.CreateWebClient();
        var page = await client.GetAsync("/Account/DevLogin");
        var token = WebTestFactory.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>
        {
            ["Input.AccessCode"] = WebTestFactory.AccessCode,
            ["Input.Role"] = "CaseManager",
            ["__RequestVerificationToken"] = token,
        };
        var login = await client.PostAsync("/Account/DevLogin", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // The (encrypted) auth cookie must not carry the API token in plaintext.
        var setCookie = string.Join(";", login.Headers.TryGetValues("Set-Cookie", out var v) ? v : []);
        Assert.DoesNotContain("manager-token", setCookie);

        // The session works: a protected page is now reachable.
        _factory.Api.RespondWith(_ => ApiResponses.CaseList());
        var casesPage = await client.GetAsync("/Cases");
        Assert.Equal(HttpStatusCode.OK, casesPage.StatusCode);
    }

    [Fact]
    public async Task NonLocalReturnUrl_RedirectsToCases()
    {
        var client = _factory.CreateWebClient();
        var page = await client.GetAsync("/Account/DevLogin");
        var token = WebTestFactory.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>
        {
            ["Input.AccessCode"] = WebTestFactory.AccessCode,
            ["Input.Role"] = "Viewer",
            ["__RequestVerificationToken"] = token,
        };
        var login = await client.PostAsync(
            "/Account/DevLogin?ReturnUrl=https%3A%2F%2Fexample.com",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Cases", login.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = await _factory.LoginAsync("Viewer");

        var page = await client.GetAsync("/Account/Logout");
        var token = WebTestFactory.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var logout = await client.PostAsync(
            "/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/Account/DevLogin", logout.Headers.Location!.ToString());
    }
}
