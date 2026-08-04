using System.Text.Json;

namespace CasePriority.Api.Tests;

/// <summary>
/// The OpenAPI document defines the Bearer scheme and marks every case operation
/// as requiring it, with documented 401/403 responses.
/// </summary>
public sealed class OpenApiSecurityTests : IClassFixture<InMemoryApiFactory>
{
    private readonly InMemoryApiFactory _factory;

    public OpenApiSecurityTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Document_DefinesBearerSecurityScheme()
    {
        using var document = await GetDocumentAsync();

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task EveryCaseOperation_RequiresBearer_AndDocuments401And403()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        var casePaths = paths.EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/cases"))
            .ToList();
        Assert.NotEmpty(casePaths);

        foreach (var path in casePaths)
        {
            var operations = path.Value.EnumerateObject()
                .Where(op => op.Name is "get" or "post" or "patch");

            foreach (var operation in operations)
            {
                Assert.True(
                    operation.Value.TryGetProperty("security", out var security),
                    $"{path.Name} {operation.Name}: missing security requirement");

                var requiresBearer = security.EnumerateArray()
                    .Any(requirement => requirement.EnumerateObject().Any(k => k.Name == "Bearer"));
                Assert.True(requiresBearer, $"{path.Name} {operation.Name}: missing Bearer requirement");

                var responses = operation.Value.GetProperty("responses");
                Assert.True(responses.TryGetProperty("401", out _), $"{path.Name} {operation.Name}: missing 401");
                Assert.True(responses.TryGetProperty("403", out _), $"{path.Name} {operation.Name}: missing 403");
            }
        }
    }
}
