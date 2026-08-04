using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace CasePriority.Api.Security;

/// <summary>
/// Renders JWT auth failures as Problem Details so 401/403 match the rest of the
/// API (and pick up the correlation traceId via CustomizeProblemDetails). A 401
/// also carries a WWW-Authenticate: Bearer challenge. Never leaks token contents,
/// signing keys, or validation-exception details.
/// </summary>
public sealed class BearerProblemDetailsEvents(IProblemDetailsService problemDetailsService)
    : JwtBearerEvents
{
    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        // Take over the response so we emit Problem Details, not the default body.
        context.HandleResponse();

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid bearer token is required.",
                Instance = context.Request.Path
            }
        });
    }

    public override async Task Forbidden(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "The authenticated identity does not have permission to perform this operation.",
                Instance = context.Request.Path
            }
        });
    }
}
