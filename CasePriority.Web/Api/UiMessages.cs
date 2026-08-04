using System.Net;
using CasePriority.Web.Api.Contracts;

namespace CasePriority.Web.Api;

/// <summary>Maps API outcomes to friendly UI text (no stack traces or raw bodies).</summary>
public static class UiMessages
{
    public const string Unavailable = "The case service is unavailable. Please try again shortly.";

    public const string StaleVersion =
        "This case changed after the page was loaded. Review the latest information and try again.";

    public const string ExpiredToken =
        "The local API token expired. Generate and store a new development token.";

    public static string ForStatus(HttpStatusCode status, ApiProblemDto? problem) => status switch
    {
        HttpStatusCode.Unauthorized => ExpiredToken,
        HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
        HttpStatusCode.PreconditionFailed => StaleVersion,
        HttpStatusCode.Conflict => problem?.Detail ?? "The requested change conflicts with the case's current state.",
        HttpStatusCode.NotFound => "That case was not found.",
        _ => problem?.Detail ?? "The request could not be completed.",
    };
}
