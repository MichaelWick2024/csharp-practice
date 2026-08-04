using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CasePriority.Api.Tests.Security;

/// <summary>
/// Creates short-lived, signed test JWTs and the matching validation parameters,
/// so the API's REAL JWT bearer handler validates signature/issuer/audience/
/// expiry against them. The signing key lives ONLY in the test assembly — never
/// in app config, appsettings, secrets, or production code.
/// </summary>
internal static class TestJwtTokens
{
    public const string Issuer = "CasePriority.Tests";
    public const string Audience = "CasePriority.Api";

    // 32+ bytes for HMAC-SHA256. Test-only; not a real credential.
    public static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("test-only-signing-key-at-least-32-bytes"));

    public static TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "name",
        RoleClaimType = "role",
    };

    public static string Create(
        IEnumerable<string>? roles = null,
        DateTime? expires = null,
        string? issuer = null,
        string? audience = null,
        SecurityKey? signingKey = null)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user"),
            new(JwtRegisteredClaimNames.Name, "Test User"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat,
                ((DateTimeOffset)now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim("role", role));
        }

        var credentials = new SigningCredentials(
            signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256);

        // Keep notBefore < expires even when creating an already-expired token
        // (the constructor rejects expires <= notBefore).
        var expiresAt = expires ?? now.AddMinutes(30);
        var notBefore = expiresAt > now ? now : expiresAt.AddMinutes(-1);

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Points the API's JWT bearer handler at the test validation params.</summary>
    public static void UseTestAuthentication(IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.MapInboundClaims = false; // keep raw "role"/"name"/"sub"
                options.TokenValidationParameters = ValidationParameters;
            });
    }
}
