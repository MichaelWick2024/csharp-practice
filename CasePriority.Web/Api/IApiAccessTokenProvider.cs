using System.Security.Claims;

namespace CasePriority.Web.Api;

/// <summary>Selects the server-held API token that matches the signed-in role.</summary>
public interface IApiAccessTokenProvider
{
    string GetToken(ClaimsPrincipal user);
}
