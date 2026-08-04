namespace CasePriority.Api.Security;

/// <summary>Role claim values the API recognizes (from the access token).</summary>
public static class CaseRoles
{
    public const string Viewer = "Viewer";
    public const string CaseManager = "CaseManager";
    public const string Administrator = "Administrator";
}
