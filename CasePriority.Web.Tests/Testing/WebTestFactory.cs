using System.Text.RegularExpressions;
using CasePriority.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CasePriority.Web.Tests.Testing;

/// <summary>
/// Boots the web app in the "Testing" environment with good config and the
/// real HTTP pipeline, but replaces the API's network handler with
/// <see cref="FakeApiHandler"/>. Also provides an antiforgery-aware login helper.
/// </summary>
public sealed class WebTestFactory : WebApplicationFactory<Program>
{
    public const string AccessCode = "test-access-code";

    public FakeApiHandler Api { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Early host config (read before ConfigureTestServices).
        builder.UseSetting("CaseApi:BaseAddress", "https://api.test");
        builder.UseSetting("CaseApi:ViewerToken", "viewer-token");
        builder.UseSetting("CaseApi:CaseManagerToken", "manager-token");
        builder.UseSetting("CaseApi:AdministratorToken", "admin-token");
        builder.UseSetting("DevelopmentLogin:AccessCode", AccessCode);

        builder.ConfigureTestServices(services =>
        {
            // Replace the outgoing network handler; keep ApiBearerTokenHandler.
            services.AddHttpClient<CaseApiClient>().ConfigurePrimaryHttpMessageHandler(() => Api);
        });
    }

    public HttpClient CreateWebClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            // Serve requests over HTTPS so the Secure, __Host- prefixed auth cookie
            // is actually stored and sent back by the client's cookie container.
            BaseAddress = new Uri("https://localhost"),
        });

    /// <summary>Logs in via the real DevLogin flow (handling antiforgery) and
    /// returns a cookie-carrying client for the given role.</summary>
    public async Task<HttpClient> LoginAsync(string role)
    {
        var client = CreateWebClient();

        var loginPage = await client.GetAsync("/Account/DevLogin");
        loginPage.EnsureSuccessStatusCode();
        var token = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>
        {
            ["Input.AccessCode"] = AccessCode,
            ["Input.Role"] = role,
            ["__RequestVerificationToken"] = token,
        };

        var response = await client.PostAsync("/Account/DevLogin", new FormUrlEncodedContent(form));
        // Success is a redirect to /Cases; the auth cookie is now on the client.
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html, """<input name="__RequestVerificationToken" type="hidden" value="([^"]+)" />""");
        Assert.True(match.Success, "Antiforgery token not found on the page.");
        return match.Groups[1].Value;
    }
}
