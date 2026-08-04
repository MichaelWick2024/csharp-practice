using System.Text.Json.Serialization;
using CasePriority.Api.Configuration;
using CasePriority.Api.ErrorHandling;
using CasePriority.Api.Health;
using CasePriority.Api.Middleware;
using CasePriority.Api.Security;
using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;
using CasePriority.Infrastructure.Health;
using CasePriority.Infrastructure.Persistence;
using CasePriority.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Request-tracing settings, validated at startup so bad config stops the app.
builder.Services
    .AddOptionsWithValidateOnStart<RequestTracingOptions>()
    .Bind(builder.Configuration.GetSection(RequestTracingOptions.SectionName))
    .Validate(
        options => IsValidHeaderName(options.HeaderName),
        "RequestTracing:HeaderName must be a valid HTTP header name.")
    .Validate(
        options => options.MaxLength is >= 16 and <= 128,
        "RequestTracing:MaxLength must be between 16 and 128.");

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize the priority enum as "Normal"/"High"/"Critical", not numbers.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<CasePriorityLevel>());
    });

// Authentication: validate JWT bearer tokens (signature/issuer/audience/expiry).
// The API never issues production tokens — validation parameters come from
// configuration (dotnet user-jwts locally) or are overridden by tests.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.EventsType = typeof(BearerProblemDetailsEvents));

builder.Services.AddScoped<BearerProblemDetailsEvents>();

// Authorization policies (roles come from the token's role claims).
builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(CasePolicies.ReadCases, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CaseRoles.Viewer, CaseRoles.CaseManager, CaseRoles.Administrator);
    })
    .AddPolicy(CasePolicies.ManageCases, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CaseRoles.CaseManager, CaseRoles.Administrator);
    });

builder.Services.AddOpenApi(options =>
{
    // Document the Bearer scheme and add a security requirement to protected ops.
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<AuthorizationOperationTransformer>();

    // The If-Match header is required for every mutation (missing -> 428). The
    // C# parameter stays nullable so our custom 428 fires instead of an
    // automatic 400 on a non-nullable binding failure — so mark it required in
    // the document explicitly to match the real contract.
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        if (operation.Parameters is not null)
        {
            foreach (var parameter in operation.Parameters)
            {
                if (parameter is OpenApiParameter p &&
                    string.Equals(p.Name, "If-Match", StringComparison.OrdinalIgnoreCase))
                {
                    p.Required = true;
                }
            }
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails(options =>
{
    // Make every Problem Details' traceId the request's correlation ID
    // (CorrelationIdMiddleware sets TraceIdentifier), so a caller can quote one
    // value that also appears in the logs.
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// EF Core / SQL Server persistence. Fail fast if the connection string is
// missing (never fall back to a default). Tests override this registration with
// the in-memory repository; migrations are applied out-of-band (CLI/CI), never
// automatically at startup.
var connectionString =
    builder.Configuration.GetConnectionString("CasePriority")
    ?? throw new InvalidOperationException("Connection string 'CasePriority' is required.");

builder.Services.AddDbContext<CasePriorityDbContext>(options =>
{
    options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure());
});

// Register the concrete EF repository once and resolve BOTH interfaces from the
// same scoped instance, so the repository and unit of work share one DbContext.
builder.Services.AddScoped<EfCaseRepository>();
builder.Services.AddScoped<ICaseRepository>(sp => sp.GetRequiredService<EfCaseRepository>());
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfCaseRepository>());
builder.Services.AddScoped<CaseService>();

// Readiness health check: real SQL Server connectivity (tagged "ready").
builder.Services
    .AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>(
        name: "sqlserver",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5)); // bounded so readiness never hangs

var app = builder.Build();

// Correlation ID first, so every downstream log and error shares it.
// Then: exception handling -> authentication (who?) -> authorization (may they?).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Health endpoints are explicitly anonymous — monitors don't carry tokens.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapControllers();

app.Run();

// Interoperable HTTP field-name check (RFC 9110 recommendation for new fields):
// begins with a letter, then letters/digits/'-'/'.'. Rejects spaces, leading
// digits, and underscores at startup rather than failing later per request.
static bool IsValidHeaderName(string? value)
{
    return !string.IsNullOrWhiteSpace(value)
        && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
}
