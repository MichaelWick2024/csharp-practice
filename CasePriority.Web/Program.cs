using CasePriority.Web.Api;
using CasePriority.Web.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---- Validated configuration (bad settings stop startup) -------------------

builder.Services
    .AddOptionsWithValidateOnStart<CaseApiOptions>()
    .Bind(builder.Configuration.GetSection(CaseApiOptions.SectionName))
    .Validate(o => o.BaseAddress is not null && o.BaseAddress.IsAbsoluteUri
        && (o.BaseAddress.Scheme == Uri.UriSchemeHttp || o.BaseAddress.Scheme == Uri.UriSchemeHttps),
        "CaseApi:BaseAddress must be an absolute http/https URL.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ViewerToken), "CaseApi:ViewerToken is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.CaseManagerToken), "CaseApi:CaseManagerToken is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AdministratorToken), "CaseApi:AdministratorToken is required.");

builder.Services
    .AddOptionsWithValidateOnStart<DevelopmentLoginOptions>()
    .Bind(builder.Configuration.GetSection(DevelopmentLoginOptions.SectionName))
    .Validate(o => (o.AccessCode?.Length ?? 0) >= 8, "DevelopmentLogin:AccessCode must be at least 8 characters.");

// ---- Development-only cookie authentication --------------------------------

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/DevLogin";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "__Host-CasePriority.Web";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddRazorPages();

// ---- Typed API client with the bearer-token + correlation handler ----------

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApiAccessTokenProvider, ApiAccessTokenProvider>();
builder.Services.AddTransient<ApiBearerTokenHandler>();

builder.Services
    .AddHttpClient<CaseApiClient>((services, client) =>
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CaseApiOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddHttpMessageHandler<ApiBearerTokenHandler>();

var app = builder.Build();

// The login is development-only — refuse to boot elsewhere without real auth.
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException(
        "CasePriority.Web currently uses development-only authentication. Configure OIDC before deployment.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
