using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CasePriority.Api.Tests;

/// <summary>
/// Proves RequestTracing options are validated at startup: valid settings boot,
/// invalid settings stop the app (AddOptionsWithValidateOnStart) rather than
/// surfacing per request.
/// </summary>
public sealed class RequestTracingOptionsValidationTests : IClassFixture<InMemoryApiFactory>
{
    private readonly InMemoryApiFactory _factory;

    public RequestTracingOptionsValidationTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> WithTracing(string headerName, string maxLength) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RequestTracing:HeaderName"] = headerName,
                    ["RequestTracing:MaxLength"] = maxLength,
                })));

    [Fact]
    public void ValidOptions_AllowStartup()
    {
        using var factory = WithTracing("X-Correlation-ID", "64");
        using var client = factory.CreateClient(); // must not throw
        Assert.NotNull(client);
    }

    [Theory]
    [InlineData("", "64")]      // blank header name
    [InlineData("X-Id", "8")]   // MaxLength below 16
    [InlineData("X-Id", "200")] // MaxLength above 128
    public void InvalidOptions_FailStartup(string headerName, string maxLength)
    {
        using var factory = WithTracing(headerName, maxLength);
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
