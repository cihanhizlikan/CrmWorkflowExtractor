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
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.ImportAsync(output, Fixture());

        Assert.True(code == ExitCode.Success, console);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        JsonElement root = manifest.RootElement;
        Assert.Contains("içe aktarma:mock-crm-export.json", root.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.All(root.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— uygun", link.GetString(), StringComparison.Ordinal));
        Assert.Equal(51, root.GetProperty("counts").GetProperty("apiCount").GetInt32());
        Assert.Equal(1, root.GetProperty("stageCounts").GetProperty("xaml.failed").GetInt32());
        Assert.Equal("ANADOLUHAYAT\\KMM2456", root.GetProperty("authenticatedUser").GetProperty("domainName").GetString());
        Assert.Equal(46, Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(Path.Combine(runRoot, BrowserExportEvidence())));
        Assert.Equal(File.ReadAllBytes(Fixture()), File.ReadAllBytes(Path.Combine(runRoot, BrowserExportEvidence())));
        Assert.Contains("'businessprocesstype' sütunu", console, StringComparison.Ordinal);
        Assert.Contains("Simulated failure for one record", console, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stage's steps live in <c>clientdata</c> and the workflows it fires live in a table of their own, and
    /// neither was being collected. Both are kept EXACTLY as CRM gave them, because what reads them is written
    /// once their real shape has been seen — so a field nobody named in advance has to survive the trip.
    /// </summary>
    [Fact]
    public async Task A_Stage_And_Its_Trigger_Reach_The_Run_Folder_Whole()
    {
        JsonNode export = JsonNode.Parse(File.ReadAllText(Fixture()))!;
        export["processStages"]![0]!["clientdata"] = "{\"steps\":[{\"stepname\":\"Tutar\"}]}";
        export["processStages"]![0]!["stageorder"] = 1;
        export["processTriggers"] = JsonNode.Parse("""
            [{"processtriggerid":"33333333-3333-3333-3333-333333333333","triggeroneventname":"Entry",
              "_processstageid_value":"11111111-1111-1111-1111-111111111111"}]
            """);
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string file = Path.Combine(output.Root, "with-stages.json");
        File.WriteAllText(file, export.ToJsonString());

        (ExitCode code, string runRoot, string console) = await RunHarness.ImportAsync(output, file);

        Assert.True(code == ExitCode.Success, console);
        JsonNode stages = JsonNode.Parse(File.ReadAllText(Path.Combine(runRoot, "ham", "surec-asamalari-ham.json")))!;
        Assert.Equal("{\"steps\":[{\"stepname\":\"Tutar\"}]}", stages[0]!["clientdata"]!.GetValue<string>());
        // A column this tool never names, kept because the rows are not filtered through a record on the way in.
        Assert.Equal(1, stages[0]!["stageorder"]!.GetValue<int>());
        JsonNode triggers = JsonNode.Parse(File.ReadAllText(Path.Combine(runRoot, "ham", "surec-tetikleyicileri.json")))!;
        Assert.Equal("Entry", triggers[0]!["triggeroneventname"]!.GetValue<string>());
        // The named index is still there beside the rows, derived from them.
        JsonNode index = JsonNode.Parse(File.ReadAllText(Path.Combine(runRoot, "ham", "surec-asamalari.json")))!;
        Assert.Equal(stages[0]!["stagename"]!.GetValue<string>(), index[0]!["name"]!.GetValue<string>());
    }

    /// <summary>
    /// An export made before this change has the stages and not the triggers. That still reprocesses — a sealed
    /// export is evidence and is never re-interpreted — but the run says plainly that the stage logic will be
    /// missing until the export is taken again, rather than quietly drawing a process with half of it absent.
    /// </summary>
    [Fact]
    public async Task An_Export_Made_Before_The_Triggers_Were_Collected_Says_So()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.ImportAsync(output, Fixture());

        Assert.True(code == ExitCode.Success, console);
        Assert.Equal("[]", File.ReadAllText(Path.Combine(runRoot, "ham", "surec-tetikleyicileri.json")).Trim());
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        Assert.Contains(manifest.RootElement.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()),
            warning => warning!.Contains("tetikleyici yok", StringComparison.Ordinal));
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

        (ExitCode code, _, string console) = await RunHarness.ImportAsync(output, tampered);

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("$count 312 iş akışı bildirdi, 51 kayıt alındı", console, StringComparison.Ordinal);
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

        (ExitCode code, string runRoot, string console) = await RunHarness.ImportAsync(output, uncounted);

        Assert.True(code == ExitCode.Success, console);
        Assert.Contains("Sunucu bağımsız bir sayım vermedi (-1 yanıtladı)", console, StringComparison.Ordinal);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        JsonElement chain = manifest.RootElement.GetProperty("countChain");
        Assert.All(chain.EnumerateArray(), link => Assert.EndsWith("— uygun", link.GetString(), StringComparison.Ordinal));
        Assert.DoesNotContain(chain.EnumerateArray(), link => link.GetString()!.Contains("$count", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_File_That_Is_Not_A_Browser_Export_Is_Refused()
    {
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string other = Path.Combine(output.Root, "workflows.json");
        File.WriteAllText(other, "{\"value\":[]}");

        (ExitCode code, _, string console) = await RunHarness.ImportAsync(output, other);

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("bir crm-browser-export/1 dosyası değil", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Missing_Import_File_Fails_Cleanly()
    {
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.ImportAsync(output, Path.Combine(output.Root, "nope.json"));

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("bulunamadı", console, StringComparison.Ordinal);
    }

    /// <summary>The bookmarklet page embeds the script. If someone edits the script and forgets to regenerate the page, the button runs old code.</summary>
    /// <summary>
    /// A request with no deadline is how an export dies silently: the production server left <c>workflows/$count</c>
    /// pending, the script waited on it forever, and the console said nothing at all. Every request now has a
    /// budget, and the count — which this server has already answered with -1 once — has a short one, because a
    /// FetchXML aggregate stands behind it.
    /// </summary>
    [Fact]
    public void The_Export_Script_Gives_Every_Request_A_Deadline()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryTree.Root().FullName, "tools", "crm-browser-export.js"));

        Assert.Contains("AbortSignal.timeout", script, StringComparison.Ordinal);
        Assert.Contains("COUNT_TIMEOUT_MS", script, StringComparison.Ordinal);
        // The count must not be the thing that stops the export: its failure falls through to the aggregate.
        Assert.Contains("$count did not answer; using the aggregate instead", script, StringComparison.Ordinal);
    }

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
        return Path.Combine("ham", "tarayici-disa-aktarim.json");
    }

    [GeneratedRegex("data-script-sha256=\"([0-9a-f]{64})\"")]
    private static partial Regex EmbeddedHash();
}
