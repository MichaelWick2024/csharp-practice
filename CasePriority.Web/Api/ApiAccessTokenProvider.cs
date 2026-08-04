using System.Security.Claims;
using CasePriority.Web.Configuration;
using Microsoft.Extensions.Options;

namespace CasePriority.Web.Api;

/// <summary>
/// Maps the local browser session's role to the matching API development token.
/// The API is still the final authority — this only picks which server-held
/// token to send.
/// </summary>
public sealed class ApiAccessTokenProvider(IOptions<CaseApiOptions> options) : IApiAccessTokenProvider
{
    private readonly CaseApiOptions _options = options.Value;

    public string GetToken(ClaimsPrincipal user)
    {
        if (user.IsInRole("Administrator"))
        {
            return _options.AdministratorToken;
        }

        if (user.IsInRole("CaseManager"))
        {
            return _options.CaseManagerToken;
        }

        if (user.IsInRole("Viewer"))
        {
            return _options.ViewerToken;
        }

        throw new InvalidOperationException("The signed-in user does not have a recognized case role.");
    }
}
