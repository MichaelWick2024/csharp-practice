using System.Text.Json.Serialization;
using CasePriority.Api.ErrorHandling;
using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize the priority enum as "Normal"/"High"/"Critical", not numbers.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<CasePriorityLevel>());
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
