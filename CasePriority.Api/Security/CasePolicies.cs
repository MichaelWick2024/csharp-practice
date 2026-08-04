namespace CasePriority.Api.Security;

/// <summary>Named authorization policies. HTTP authorization is an API concern —
/// the domain never knows about JWTs, roles, or policies.</summary>
public static class CasePolicies
{
    public const string ReadCases = "Cases.Read";
    public const string ManageCases = "Cases.Manage";
}
