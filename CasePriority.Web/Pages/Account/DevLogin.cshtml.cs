using System.Security.Claims;
using CasePriority.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace CasePriority.Web.Pages.Account;

/// <summary>
/// DEVELOPMENT-ONLY sign-in: an access code + a role, exchanged for a cookie
/// session. The API bearer token is never placed in claims, the cookie, HTML,
/// or logs — the server selects it per request from the session's role.
/// </summary>
[AllowAnonymous]
public sealed class DevLoginModel(IOptions<DevelopmentLoginOptions> options) : PageModel
{
    private static readonly string[] ValidRoles = ["Viewer", "CaseManager", "Administrator"];
    private readonly DevelopmentLoginOptions _options = options.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required]
        public string AccessCode { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Viewer";
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(Input.AccessCode, _options.AccessCode, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Invalid access code.");
            return Page();
        }

        if (!ValidRoles.Contains(Input.Role))
        {
            ModelState.AddModelError("Input.Role", "Select a valid role.");
            return Page();
        }

        // Only identity claims — never the API token.
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "Local Demo User"),
            new(ClaimTypes.Role, Input.Role),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return LocalRedirect(ReturnUrl ?? "/Cases");
    }
}
