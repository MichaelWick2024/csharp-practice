using System.Net.Http.Headers;

namespace CasePriority.Web.Api;

/// <summary>
/// Attaches the role-appropriate API bearer token to every outgoing request and
/// propagates the browser request's correlation ID, so one value spans
/// browser -> web logs -> API logs -> Problem Details.
/// </summary>
public sealed class ApiBearerTokenHandler(
    IHttpContextAccessor httpContextAccessor,
    IApiAccessTokenProvider tokenProvider)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP request was found.");

        var token = tokenProvider.GetToken(httpContext.User);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Headers.TryAddWithoutValidation("X-Correlation-ID", httpContext.TraceIdentifier);

        return base.SendAsync(request, cancellationToken);
    }
}
