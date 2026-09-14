using Crm.Cli;
using Crm.Cli.Configuration;
using Crm.Extract.Http;
using Crm.Tests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("__CRM_PASSWORD__")]
    [InlineData("")]
    public void The_Pipeline_Placeholder_Is_Not_A_Password(string configured)
    {
        CrmConnectionOptions options = new() { Password = configured };

        Assert.Null(options.ConfiguredPassword());
    }

    [Fact]
    public void An_Empty_Web_Api_Url_Is_Rejected_Before_Anything_Is_Contacted()
    {
        ValidateOptionsResult result = new CrmConnectionOptionsValidator().Validate(Options.DefaultName, new CrmConnectionOptions());

        Assert.True(result.Failed);
    }

    [Fact]
    public void Explicit_Authentication_Requires_User_And_Domain()
    {
        CrmConnectionOptions options = new() { WebApiBaseUrl = FakeCrmServer.BaseUrl, Authentication = CrmAuthenticationMode.Explicit };

        ValidateOptionsResult result = new CrmConnectionOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Appsettings_Overrides_The_Program_Constants()
    {
        using TemporaryOutput folder = new();
        Directory.CreateDirectory(folder.Root);
        File.WriteAllText(Path.Combine(folder.Root, "appsettings.json"), "{\"Crm\":{\"PageSize\":50}}");

        ExtractorSettings settings = ExtractorSettings.Bind(ExtractorSettings.BuildConfiguration(
            new Dictionary<string, string?> { ["Crm:PageSize"] = "20", ["Crm:WebApiBaseUrl"] = FakeCrmServer.BaseUrl }, folder.Root));

        Assert.Equal(50, settings.Crm.Value.PageSize);
        Assert.Equal(FakeCrmServer.BaseUrl, settings.Crm.Value.WebApiBaseUrl);
    }

    /// <summary>§10: zero arguments, and the committed appsettings.json (empty URL) must stop cleanly, not crash.</summary>
    [Fact]
    public async Task Main_With_The_Committed_Settings_Exits_As_Configuration_Invalid()
    {
        int code = await Program.Main();

        Assert.Equal((int)ExitCode.ConfigurationInvalid, code);
    }
}
