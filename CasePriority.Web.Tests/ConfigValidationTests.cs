using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace CasePriority.Web.Tests;

public sealed class ConfigValidationTests
{
    private static WebApplicationFactory<Program> FactoryWith(Action<Dictionary<string, string?>> mutate)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CaseApi:BaseAddress"] = "https://api.test",
            ["CaseApi:ViewerToken"] = "viewer",
            ["CaseApi:CaseManagerToken"] = "manager",
            ["CaseApi:AdministratorToken"] = "admin",
            ["DevelopmentLogin:AccessCode"] = "test-access-code",
        };
        mutate(settings);

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in settings)
            {
                if (value is not null)
                {
                    builder.UseSetting(key, value);
                }
            }
        });
    }

    [Fact]
    public void ValidConfig_AllowsStartup()
    {
        using var factory = FactoryWith(_ => { });
        using var client = factory.CreateClient();
        Assert.NotNull(client);
    }

    [Fact]
    public void MissingBaseAddress_FailsStartup()
    {
        // Empty overrides the appsettings.json default, leaving BaseAddress unbound.
        using var factory = FactoryWith(s => s["CaseApi:BaseAddress"] = "");
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public void RelativeBaseAddress_FailsStartup()
    {
        using var factory = FactoryWith(s => s["CaseApi:BaseAddress"] = "/relative");
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public void MissingRoleToken_FailsStartup()
    {
        using var factory = FactoryWith(s => s["CaseApi:CaseManagerToken"] = "");
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public void ShortAccessCode_FailsStartup()
    {
        using var factory = FactoryWith(s => s["DevelopmentLogin:AccessCode"] = "short");
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
