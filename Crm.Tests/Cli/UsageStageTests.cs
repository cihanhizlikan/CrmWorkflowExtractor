using System.Text;
using System.Text.Json;
using Crm.Cli;
using Crm.Cli.Stages;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>Usage evidence (tools/crm-usage-export.js) and drafts held apart from grouping, over the synthetic browser fixtures.</summary>
public sealed class UsageStageTests
{
    private static readonly Guid DefinitionThatRan = Guid.Parse("00000000-0000-0000-0000-000000000000");

    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", name);
    }

    [Fact]
    public async Task Usage_Evidence_Is_Kept_Reported_And_Drafts_Are_Held_Apart_From_Grouping()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        Assert.True(code == ExitCode.Success, console);
        Assert.Equal(File.ReadAllBytes(Fixture("mock-usage-export.json")), File.ReadAllBytes(Path.Combine(runRoot, "raw", "usage-export.json")));
        Assert.Contains("1 failed lookup(s)", console, StringComparison.Ordinal);

        string usage = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "reports", "usage.csv")));
        string ran = Assert.Single(usage.Split("\r\n"), line => line.Contains(DefinitionThatRan.ToString("D"), StringComparison.Ordinal));
        Assert.Contains("2026-09-20", ran, StringComparison.Ordinal);
        Assert.Contains("Used: last logged run 2026-09-20 (system job)", ran, StringComparison.Ordinal);
        Assert.Contains("No logged run; the oldest run seen anywhere is 2026-09-20: NOT proof of non-use", usage, StringComparison.Ordinal);
        Assert.Contains("Draft: cannot start new runs", usage, StringComparison.Ordinal);

        // 44 drafts in the fixture, one without XAML (the simulated failure), so 43 IR documents: listed on their own, absent from clusters.csv, and the count chain still balances.
        string drafts = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "clusters", "drafts.csv")));
        Assert.Equal(1 + 43, drafts.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        string clusters = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "clusters", "clusters.csv")));
        Assert.DoesNotContain("Hasar Onay", clusters, StringComparison.Ordinal);
        Assert.Contains("2026-09-20", clusters, StringComparison.Ordinal);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        Assert.All(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— ok", link.GetString(), StringComparison.Ordinal));
        Assert.Contains(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => link.GetString()!.Contains("workflows placed in a cluster + drafts held apart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Reprocessed_Run_Inherits_The_Usage_Evidence_Of_Its_Source_Run()
    {
        using TemporaryOutput output = new();
        (_, string first, _) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        (ExitCode code, string second, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, reprocessRunId: Path.GetFileName(first));

        Assert.True(code == ExitCode.Success, console);
        Assert.Contains("Used: last logged run 2026-09-20", File.ReadAllText(Path.Combine(second, "reports", "usage.csv")), StringComparison.Ordinal);
    }

    /// <summary>Asking the server for the oldest record of all sorts the whole System Job table; production leaves it pending.</summary>
    [Fact]
    public void The_Export_Script_Never_Sorts_A_Whole_Table()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryTree.Root().FullName, "tools", "crm-usage-export.js"));

        Assert.DoesNotContain("createdon asc", script, StringComparison.Ordinal);
        Assert.Contains("AbortSignal.timeout", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Missing_Usage_File_Fails_Cleanly()
    {
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Path.Combine(output.Root, "nowhere.json"));

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("Run:UsageFile", console, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DRAFT_OPEN_PHONECALL", true)]
    [InlineData("*draft*case*step", true)]
    [InlineData("18970_DEBUG_TEST", true)]
    [InlineData("Poliçe Deneme Akışı", true)]
    [InlineData("ESKİ - Hasar Onay", true)]
    [InlineData("Poliçe İptal Süreci", false)]
    [InlineData("Contest Kampanya", false)]
    [InlineData("Template Onay", false)]
    public void A_Test_Like_Name_Is_Recognised_As_A_Hint(string name, bool expected)
    {
        Assert.Equal(expected, UsageStage.NameSuggestsTest(name));
    }

    [Fact]
    public void No_Logged_Run_Is_Never_Reported_As_Unused()
    {
        UsageEvidence usage = new(DateTimeOffset.Parse("2026-06-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), null, "", new Dictionary<Guid, WorkflowUsage>());

        Assert.StartsWith("Unknowable", UsageStage.Verdict(Identity("Business Rule", "Background", "Activated"), usage), StringComparison.Ordinal);
        Assert.StartsWith("No failure logged", UsageStage.Verdict(Identity("Workflow", "Real-time", "Activated"), usage), StringComparison.Ordinal);
        Assert.Contains("NOT proof of non-use", UsageStage.Verdict(Identity("Workflow", "Background", "Activated"), usage), StringComparison.Ordinal);
        Assert.Contains("NOT proof of non-use", UsageStage.Verdict(Identity("Dialog", "Background", "Activated"), usage), StringComparison.Ordinal);
        Assert.StartsWith("Draft", UsageStage.Verdict(Identity("Workflow", "Background", "Draft"), usage), StringComparison.Ordinal);
    }

    private static WorkflowIdentity Identity(string category, string mode, string state)
    {
        return new WorkflowIdentity(Guid.NewGuid(), "x", null, category, "Definition", "new_policy", mode, "Organization", state, false, false, null, null, null, 1, true);
    }
}
