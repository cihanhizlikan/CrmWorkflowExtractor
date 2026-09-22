using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Crm.Cli;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// Run:ImportFile over a file the real browser script produced (see Fixtures/BrowserExport/README.md). The fake
/// server is passed only to prove it is never contacted.
/// </summary>
public sealed partial class BrowserExportImportTests
{
    private static string Fixture()
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json");
    }

    [Fact]
    public async Task A_Browser_Export_Runs_Every_Stage_Without_Touching_The_Network_And_The_Count_Chain_Balances()
    {
        FakeCrmServer silent = new();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(silent, output, importFile: Fixture());

        Assert.True(code == ExitCode.Success, console);
        Assert.Empty(silent.Requests);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        JsonElement root = manifest.RootElement;
        Assert.Contains("import:mock-crm-export.json", root.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.All(root.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— ok", link.GetString(), StringComparison.Ordinal));
        Assert.Equal(51, root.GetProperty("counts").GetProperty("apiCount").GetInt32());
        Assert.Equal(1, root.GetProperty("stageCounts").GetProperty("xaml.failed").GetInt32());
        Assert.Equal("ANADOLUHAYAT\\KMM2456", root.GetProperty("authenticatedUser").GetProperty("domainName").GetString());
        Assert.Equal(46, Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(Path.Combine(runRoot, BrowserExportEvidence())));
        Assert.Equal(File.ReadAllBytes(Fixture()), File.ReadAllBytes(Path.Combine(runRoot, BrowserExportEvidence())));
        Assert.Contains("Column 'businessprocesstype'", console, StringComparison.Ordinal);
        Assert.Contains("Simulated failure for one record", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Export_Whose_Count_Does_Not_Match_Its_Records_Fails()
    {
        JsonNode export = JsonNode.Parse(File.ReadAllText(Fixture()))!;
        export["count"] = 312;
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string tampered = Path.Combine(output.Root, "tampered.json");
        File.WriteAllText(tampered, export.ToJsonString());

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: tampered);

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("$count reported 312 workflows but 51 were retrieved", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Export_Whose_Server_Gave_No_Count_Succeeds_With_A_Warning_And_No_Records_Link()
    {
        // The production server answered workflows/$count with -1; an export made before the aggregate fallback
        // carries that -1 and must still import.
        JsonNode export = JsonNode.Parse(File.ReadAllText(Fixture()))!;
        export["count"] = -1;
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string uncounted = Path.Combine(output.Root, "uncounted.json");
        File.WriteAllText(uncounted, export.ToJsonString());

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: uncounted);

        Assert.True(code == ExitCode.Success, console);
        Assert.Contains("The server gave no independent count (it answered -1)", console, StringComparison.Ordinal);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        JsonElement chain = manifest.RootElement.GetProperty("countChain");
        Assert.All(chain.EnumerateArray(), link => Assert.EndsWith("— ok", link.GetString(), StringComparison.Ordinal));
        Assert.DoesNotContain(chain.EnumerateArray(), link => link.GetString()!.Contains("$count", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_File_That_Is_Not_A_Browser_Export_Is_Refused()
    {
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string other = Path.Combine(output.Root, "workflows.json");
        File.WriteAllText(other, "{\"value\":[]}");

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: other);

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("is not a crm-browser-export/1 file", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Missing_Import_File_Fails_Cleanly()
    {
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: Path.Combine(output.Root, "nope.json"));

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("does not exist", console, StringComparison.Ordinal);
    }

    /// <summary>The bookmarklet page embeds the script. If someone edits the script and forgets to regenerate the page, the button runs old code.</summary>
    [Theory]
    [InlineData("crm-browser-export.js", "crm-export.html")]
    [InlineData("crm-usage-export.js", "crm-usage.html")]
    public void The_Bookmarklet_Page_Was_Generated_From_The_Current_Script(string script, string pageName)
    {
        string tools = Path.Combine(RepositoryTree.Root().FullName, "tools");
        string scriptHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(tools, script))));
        string page = File.ReadAllText(Path.Combine(tools, pageName));

        Match embedded = EmbeddedHash().Match(page);

        Assert.True(embedded.Success, $"{pageName} has no data-script-sha256.");
        Assert.True(embedded.Groups[1].Value == scriptHash,
            $"tools/{pageName} is stale — run: node tools/build-export-page.js");
    }

    private static string BrowserExportEvidence()
    {
        return Path.Combine("raw", "browser-export.json");
    }

    [GeneratedRegex("data-script-sha256=\"([0-9a-f]{64})\"")]
    private static partial Regex EmbeddedHash();
}
