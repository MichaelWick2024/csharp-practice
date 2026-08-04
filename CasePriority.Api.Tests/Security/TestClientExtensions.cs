using System.Net.Http.Headers;
using CasePriority.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CasePriority.Api.Tests.Security;

internal static class TestClientExtensions
{
    /// <summary>A client whose default Authorization header carries a signed
    /// test JWT with the given roles (Administrator by default).</summary>
    public static HttpClient CreateAuthenticatedClient(
        this WebApplicationFactory<Program> factory, params string[] roles)
    {
        var effectiveRoles = roles.Length == 0 ? [CaseRoles.Administrator] : roles;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokens.Create(roles: effectiveRoles));
        return client;
    }
}
