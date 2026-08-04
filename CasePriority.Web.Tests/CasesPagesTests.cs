using System.Net;
using CasePriority.Web.Tests.Testing;

namespace CasePriority.Web.Tests;

public sealed class CasesPagesTests : IClassFixture<WebTestFactory>
{
    private readonly WebTestFactory _factory;

    public CasesPagesTests(WebTestFactory factory)
    {
        _factory = factory;
    }

    private static async Task<string> Antiforgery(HttpClient client, string url)
    {
        var html = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        return WebTestFactory.ExtractAntiforgeryToken(html);
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields)
    {
        var data = fields.ToDictionary(f => f.Key, f => f.Value);
        data["__RequestVerificationToken"] = token;
        return new FormUrlEncodedContent(data);
    }

    // ---- Access ------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_Cases_RedirectsToLogin()
    {
        var response = await _factory.CreateWebClient().GetAsync("/Cases");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/DevLogin", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Viewer_CanViewList()
    {
        _factory.Api.RespondWith(_ => ApiResponses.CaseList(("WEB-0001", 1)));
        var client = await _factory.LoginAsync("Viewer");

        var response = await client.GetAsync("/Cases");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("WEB-0001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Viewer_CannotOpenCreate()
    {
        var client = await _factory.LoginAsync("Viewer");
        var response = await client.GetAsync("/Cases/Create");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Manager_CanOpenCreate()
    {
        var client = await _factory.LoginAsync("CaseManager");
        var response = await client.GetAsync("/Cases/Create");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Create ------------------------------------------------------------

    [Fact]
    public async Task Create_Valid_RedirectsToDetails()
    {
        _factory.Api.RespondWith(_ => ApiResponses.Case("WEB-0001", 1, HttpStatusCode.Created));
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Create");

        var response = await client.PostAsync("/Cases/Create", Form(token,
            ("Input.CaseNumber", "WEB-0001"), ("Input.Subject", "Portal down"), ("Input.Severity", "3")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Cases/Details/WEB-0001", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Create_InvalidForm_RedisplaysValidation()
    {
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Create");

        // Empty case number -> client-side ModelState invalid, API not called.
        var response = await client.PostAsync("/Cases/Create", Form(token,
            ("Input.CaseNumber", ""), ("Input.Subject", "x"), ("Input.Severity", "3")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_WithoutAntiforgeryToken_IsRejected()
    {
        var client = await _factory.LoginAsync("CaseManager");
        var response = await client.PostAsync("/Cases/Create", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Input.CaseNumber"] = "X", ["Input.Subject"] = "y", ["Input.Severity"] = "3" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Details + mutations ----------------------------------------------

    [Fact]
    public async Task Details_RendersVersion()
    {
        _factory.Api.RespondWith(_ => ApiResponses.Case("WEB-0001", 7));
        var client = await _factory.LoginAsync("Viewer");

        var html = await (await client.GetAsync("/Cases/Details/WEB-0001")).Content.ReadAsStringAsync();
        Assert.Contains(">7<", html); // version rendered
    }

    [Fact]
    public async Task Close_SendsCurrentEtag_AsIfMatch()
    {
        _factory.Api.RespondWith(request =>
            request.Method == HttpMethod.Patch
                ? ApiResponses.Case("WEB-0001", 2)
                : ApiResponses.Case("WEB-0001", 1));
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Details/WEB-0001");

        var response = await client.PostAsync("/Cases/Details/WEB-0001?handler=Close",
            Form(token, ("CaseNumber", "WEB-0001"), ("EntityTag", "\"1\"")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("\"1\"", _factory.Api.LastRequest!.Headers.GetValues("If-Match").Single());
    }

    [Fact]
    public async Task StaleUpdate_ShowsReloadMessage()
    {
        _factory.Api.RespondWith(request =>
            request.Method == HttpMethod.Patch
                ? ApiResponses.Problem(HttpStatusCode.PreconditionFailed, "Precondition failed", "stale",
                    ",\"expectedVersion\":1,\"currentVersion\":2")
                : ApiResponses.Case("WEB-0001", 2));
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Details/WEB-0001");

        var post = await client.PostAsync("/Cases/Details/WEB-0001?handler=Close",
            Form(token, ("CaseNumber", "WEB-0001"), ("EntityTag", "\"1\"")));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var followed = await client.GetAsync(post.Headers.Location!.ToString());
        Assert.Contains("changed after the page was loaded", await followed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidTransition_ShowsConflictMessage()
    {
        _factory.Api.RespondWith(request =>
            request.Method == HttpMethod.Patch
                ? ApiResponses.Problem(HttpStatusCode.Conflict, "Request conflict", "Case WEB-0001 is already closed.")
                : ApiResponses.Case("WEB-0001", 1, isOpen: false));
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Details/WEB-0001");

        var response = await client.PostAsync("/Cases/Details/WEB-0001?handler=Close",
            Form(token, ("CaseNumber", "WEB-0001"), ("EntityTag", "\"1\"")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already closed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExpiredApiToken_ShowsExpiredMessage()
    {
        _factory.Api.RespondWith(_ => ApiResponses.Problem(HttpStatusCode.Unauthorized, "Unauthorized", "expired"));
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Details/WEB-0001");

        var response = await client.PostAsync("/Cases/Details/WEB-0001?handler=Escalate",
            Form(token, ("CaseNumber", "WEB-0001"), ("EntityTag", "\"1\"")));

        Assert.Contains("local API token expired", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiForbidden_OnMutation_RedirectsToAccessDenied()
    {
        _factory.Api.RespondWith(request =>
            request.Method == HttpMethod.Patch
                ? ApiResponses.Problem(HttpStatusCode.Forbidden, "Forbidden", "no")
                : ApiResponses.Case("WEB-0001", 1));
        var client = await _factory.LoginAsync("CaseManager");
        var token = await Antiforgery(client, "/Cases/Details/WEB-0001");

        var response = await client.PostAsync("/Cases/Details/WEB-0001?handler=Close",
            Form(token, ("CaseNumber", "WEB-0001"), ("EntityTag", "\"1\"")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ApiUnavailable_ShowsFriendlyMessage()
    {
        _factory.Api.RespondWith((_, _) => throw new HttpRequestException("down"));
        var client = await _factory.LoginAsync("Viewer");

        var html = await (await client.GetAsync("/Cases")).Content.ReadAsStringAsync();
        Assert.Contains("service is unavailable", html);
    }

    // ---- Token isolation + presentation -----------------------------------

    [Fact]
    public async Task ApiToken_NeverAppearsInRenderedHtml()
    {
        _factory.Api.RespondWith(_ => ApiResponses.CaseList(("WEB-0001", 1)));
        var client = await _factory.LoginAsync("CaseManager");

        var html = await (await client.GetAsync("/Cases")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("manager-token", html);
    }

    [Fact]
    public async Task ManageControls_VisibleForManager_HiddenForViewer()
    {
        _factory.Api.RespondWith(_ => ApiResponses.CaseList(("WEB-0001", 1)));

        var manager = await _factory.LoginAsync("CaseManager");
        Assert.Contains("New case", await (await manager.GetAsync("/Cases")).Content.ReadAsStringAsync());

        var viewer = await _factory.LoginAsync("Viewer");
        Assert.DoesNotContain("New case", await (await viewer.GetAsync("/Cases")).Content.ReadAsStringAsync());
    }
}
