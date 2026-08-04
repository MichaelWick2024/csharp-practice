using System.Text.Json.Serialization;
using CasePriority.Api.ErrorHandling;
using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;
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

// One shared, thread-safe repository across requests (so POSTed cases persist
// for later GETs). Scoped service, created once per request, coordinates
// against that shared repository.
builder.Services.AddSingleton<ICaseRepository, InMemoryCaseRepository>();
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
