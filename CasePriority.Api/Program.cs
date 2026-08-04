using System.Text.Json.Serialization;
using CasePriority.Api.ErrorHandling;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;
// Alias the enum, whose name collides with the CasePriority root namespace.
using Priority = CasePriority.Core.Domain.CasePriority;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize the priority enum as "Normal"/"High"/"Critical", not numbers.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<Priority>());
    });

builder.Services.AddOpenApi();
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

// Exposes the generated top-level Program type to the integration-test project
// (WebApplicationFactory<Program>).
public partial class Program;
