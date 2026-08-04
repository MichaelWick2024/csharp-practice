namespace CasePriority.Web.Configuration;

/// <summary>
/// The shared access code for the DEVELOPMENT-ONLY login. A real deployment
/// replaces this with an OIDC confidential-client sign-in.
/// </summary>
public sealed class DevelopmentLoginOptions
{
    public const string SectionName = "DevelopmentLogin";

    public string AccessCode { get; init; } = string.Empty;
}
