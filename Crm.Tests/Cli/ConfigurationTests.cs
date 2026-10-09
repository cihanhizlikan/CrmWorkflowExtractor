using System.Text.Json;
using Crm.Cli;
using Crm.Cli.Configuration;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// §10: zero arguments; every setting from configuration. What there is to configure is where the files are — the
/// export, the usage file, the logo — and where the output goes. There are no connection settings and no secret:
/// the tool reads files and nothing else.
/// </summary>
public sealed class ConfigurationTests
{
    [Fact]
    public void Appsettings_Overrides_The_Program_Constants()
    {
        using TemporaryOutput folder = new();
        Directory.CreateDirectory(folder.Root);
        File.WriteAllText(Path.Combine(folder.Root, "appsettings.json"), "{\"Run\":{\"UsageFile\":\"D:/exports/kullanim.json\"}}");

        ExtractorSettings settings = ExtractorSettings.Bind(ExtractorSettings.BuildConfiguration(
            new Dictionary<string, string?> { ["Run:UsageFile"] = "", ["Run:ImportFile"] = "D:/exports/disa-aktarim.json" }, folder.Root));

        Assert.Equal("D:/exports/kullanim.json", settings.Run.Value.UsageFile);
        Assert.Equal("D:/exports/disa-aktarim.json", settings.Run.Value.ImportFile);
    }

    /// <summary>
    /// Every file a run reads has its key in the committed <c>appsettings.json</c>, so a person on the locked-down
    /// host fills in a value instead of having to know a key exists. <c>UsageFile</c> was missing from it until
    /// 2026-10-09 — bound in code, listed in <c>Program</c>, and absent from the one file anyone edits.
    /// </summary>
    [Fact]
    public void The_Committed_Settings_Name_Every_File_A_Run_Reads_And_No_Connection()
    {
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        JsonElement run = settings.RootElement.GetProperty("Run");

        foreach (string key in new[] { "ImportFile", "UsageFile", "ReprocessRunId", "LogoFile" })
        {
            Assert.True(run.TryGetProperty(key, out _), $"appsettings.json has no Run:{key}");
        }
        Assert.False(settings.RootElement.TryGetProperty("Crm", out _), "appsettings.json still carries connection settings");
    }

    /// <summary>The committed settings name nothing to read, so a run must stop cleanly — no crash, no run folder.</summary>
    [Fact]
    public async Task Main_With_The_Committed_Settings_Exits_As_Configuration_Invalid()
    {
        int code = await Program.Main();

        Assert.Equal((int)ExitCode.ConfigurationInvalid, code);
    }
}
