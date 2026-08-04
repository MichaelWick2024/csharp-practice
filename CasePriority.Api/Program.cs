using System.Text.Json.Serialization;
using CasePriority.Api.ErrorHandling;
using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;
using CasePriority.Infrastructure.Persistence;
using CasePriority.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize the priority enum as "Normal"/"High"/"Critical", not numbers.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<CasePriorityLevel>());
    });

builder.Services.AddOpenApi(options =>
{
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
builder.Services.AddProblemDetails();
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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();
