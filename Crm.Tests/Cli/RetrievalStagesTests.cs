using System.Text.Json;
using Crm.Cli;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class RetrievalStagesTests
{
    [Fact]
    public async Task Every_Definition_And_Activation_Gets_Its_Xaml_Written_Unmodified()
    {
        FakeOrganization organization = new() { WorkflowCount = 10 };
        FakeCrmServer server = organization.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        IReadOnlyList<XamlEntry> index = XamlEntry.ReadIndex(runRoot);
        Assert.Equal(10, index.Count);
        Assert.All(index, entry => Assert.Equal(XamlEntry.SourceFetched, entry.Source));
        string expected = FakeOrganization.DefaultXaml(4, false);
        Assert.Equal(expected, File.ReadAllText(Path.Combine(runRoot, "ham", "xaml", FakeOrganization.WorkflowId(4).ToString("D") + ".xaml")));
    }

    /// <summary>The M2 gate: run twice; the second run refetches nothing unchanged and produces identical hashes.</summary>
    [Fact]
    public async Task A_Second_Run_Reuses_Unchanged_Xaml_With_Identical_Hashes()
    {
        using TemporaryOutput output = new();
        (_, string firstRoot, _) = await RunHarness.RunAsync(new FakeOrganization { WorkflowCount = 10 }.Build(), output);
        FakeCrmServer second = new FakeOrganization { WorkflowCount = 10 }.Build();

        (ExitCode code, string secondRoot, _) = await RunHarness.RunAsync(second, output);

        Assert.Equal(ExitCode.Success, code);
        Assert.NotEqual(firstRoot, secondRoot);
        Assert.DoesNotContain(second.Requests, request => request.Uri.AbsolutePath.Contains("/workflows(", StringComparison.Ordinal));
        IReadOnlyList<XamlEntry> before = XamlEntry.ReadIndex(firstRoot);
        IReadOnlyList<XamlEntry> after = XamlEntry.ReadIndex(secondRoot);
        Assert.Equal(before.Select(entry => entry.Sha256), after.Select(entry => entry.Sha256));
        Assert.All(after, entry => Assert.StartsWith("reused:", entry.Source, StringComparison.Ordinal));
        Assert.DoesNotContain(second.Requests, request => request.Uri.AbsolutePath.Contains("EntityDefinitions(LogicalName='new_policy')", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Changed_Version_Is_Fetched_Again()
    {
        using TemporaryOutput output = new();
        await RunHarness.RunAsync(new FakeOrganization { WorkflowCount = 4 }.Build(), output);
        FakeCrmServer second = new FakeOrganization { WorkflowCount = 4, VersionOffset = 1 }.Build();

        (_, string secondRoot, _) = await RunHarness.RunAsync(second, output);

        Assert.All(XamlEntry.ReadIndex(secondRoot), entry => Assert.Equal(XamlEntry.SourceFetched, entry.Source));
    }

    [Fact]
    public async Task Hand_Authored_Xaml_Goes_To_Manual_Review()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 6, NonDesigner = new HashSet<int> { 2 } }.Build();
        using TemporaryOutput output = new();

        (_, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Assert.True(File.Exists(Path.Combine(runRoot, "elle-inceleme", FakeOrganization.WorkflowId(2).ToString("D") + ".xaml")));
        Assert.Contains("Poliçe İptal Süreci 2", File.ReadAllText(Path.Combine(runRoot, "elle-inceleme", "dizin.md")), StringComparison.Ordinal);
        Assert.Equal(1, Counts(runRoot)["manualReview"]);
    }

    [Fact]
    public async Task Drift_Separates_Real_Logic_Changes_From_Identifier_Differences()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 6, Drifted = new HashSet<int> { 2 } }.Build();
        using TemporaryOutput output = new();

        (_, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Dictionary<string, int> counts = Counts(runRoot);
        Assert.Equal(3, counts["drift.pairsCompared"]);
        Assert.Equal(1, counts["drift.structureDiffers"]);
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains(plan.Rows("Sapma"), row => row.Count > 3
            && row[2] == "evet" && row[3] == FakeOrganization.WorkflowId(2).ToString("D"));
    }

    [Fact]
    public async Task A_Failed_Xaml_Request_Is_Warned_And_The_Rest_Continue()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 6, XamlMissing = new HashSet<int> { 3 } }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        Assert.Equal(5, XamlEntry.ReadIndex(runRoot).Count);
        Assert.Equal(1, Counts(runRoot)["xaml.failed"]);
        Assert.Contains("alınamadı", console, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_Xaml_Ignores_Attribute_Order_Formatting_And_Class_Names()
    {
        string left = "<a x:Class=\"XrmWorkflow0123456789abcdef0123456789abcdef\" b=\"1\" c=\"2\" xmlns:x=\"urn:x\"><d/></a>";
        string right = "<a  c=\"2\"\n b=\"1\" x:Class=\"XrmWorkflowfedcba9876543210fedcba9876543210\" xmlns:x=\"urn:x\">\n  <d />\n</a>";

        Assert.Equal(DriftAnalyzer.Canonical(left), DriftAnalyzer.Canonical(right));
    }

    private static Dictionary<string, int> Counts(string runRoot)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        return manifest.RootElement.GetProperty("stageCounts").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32());
    }
}
