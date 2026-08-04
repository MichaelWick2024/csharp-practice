namespace CasePriority.Web.Configuration;

/// <summary>
/// Where the API is and which server-held development token to use per role.
/// Tokens are secrets (user-secrets) — only <see cref="BaseAddress"/> lives in
/// appsettings. They never reach the browser.
/// </summary>
public sealed class CaseApiOptions
{
    public const string SectionName = "CaseApi";

    public Uri? BaseAddress { get; init; }

    public string ViewerToken { get; init; } = string.Empty;

    public string CaseManagerToken { get; init; } = string.Empty;

    public string AdministratorToken { get; init; } = string.Empty;
}
