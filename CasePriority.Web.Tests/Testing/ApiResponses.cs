using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CasePriority.Web.Tests.Testing;

/// <summary>Builds canned API JSON responses (case bodies, Problem Details) with ETags.</summary>
internal static class ApiResponses
{
    public static HttpResponseMessage Case(
        string caseNumber, long version, HttpStatusCode status = HttpStatusCode.OK,
        int severity = 3, bool isOpen = true, bool escalated = false, string priority = "High")
    {
        var json = $$"""
            {"caseNumber":"{{caseNumber}}","subject":"Subject","severity":{{severity}},
             "isOpen":{{(isOpen ? "true" : "false")}},"isExecutiveEscalation":{{(escalated ? "true" : "false")}},
             "priority":"{{priority}}","version":{{version}}}
            """;
        var response = new HttpResponseMessage(status) { Content = Json(json) };
        response.Headers.ETag = new EntityTagHeaderValue($"\"{version}\"");
        return response;
    }

    public static HttpResponseMessage CaseList(params (string CaseNumber, long Version)[] cases)
    {
        var items = string.Join(",", cases.Select(c =>
            $$"""{"caseNumber":"{{c.CaseNumber}}","subject":"Subject","severity":3,"isOpen":true,"isExecutiveEscalation":false,"priority":"High","version":{{c.Version}}}"""));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = Json($"[{items}]") };
    }

    public static HttpResponseMessage Problem(
        HttpStatusCode status, string title, string detail, string extraJson = "")
    {
        var json = $$"""
            {"title":"{{title}}","status":{{(int)status}},"detail":"{{detail}}","traceId":"trace-xyz"{{extraJson}}}
            """;
        return new HttpResponseMessage(status) { Content = Json(json) };
    }

    public static HttpResponseMessage ValidationProblem(string field, string message) =>
        new(HttpStatusCode.BadRequest)
        {
            Content = Json($$$"""
                {"title":"Invalid request","status":400,"errors":{"{{{field}}}":["{{{message}}}"]}}
                """)
        };

    private static StringContent Json(string json) =>
        new(json, Encoding.UTF8, "application/json");
}
