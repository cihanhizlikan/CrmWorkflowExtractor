using System.Text.Json;
using Crm.Cli;
using Crm.Cli.Stages;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class XamlStageTests
{
    [Fact]
    public async Task Every_Definition_And_Activation_Gets_Its_Xaml_Written_Unmodified()
    {
        FakeOrganization organization = new() { WorkflowCount = 10 };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(organization, output);

        Assert.Equal(ExitCode.Success, code);
        IReadOnlyList<XamlEntry> index = XamlEntry.ReadIndex(runRoot);
        Assert.Equal(10, index.Count);
        Assert.All(index, entry => Assert.Equal(BrowserExportImport.SourceBrowserExport, entry.Source));
        string expected = FakeOrganization.DefaultXaml(4, false);
        Assert.Equal(expected, File.ReadAllText(Path.Combine(runRoot, "ham", "xaml", FakeOrganization.WorkflowId(4).ToString("D") + ".xaml")));
    }

    [Fact]
    public async Task Hand_Authored_Xaml_Goes_To_Manual_Review()
    {
        FakeOrganization organization = new() { WorkflowCount = 6, NonDesigner = new HashSet<int> { 2 } };
        using TemporaryOutput output = new();

        (_, string runRoot, _) = await RunHarness.RunAsync(organization, output);

        Assert.True(File.Exists(Path.Combine(runRoot, "elle-inceleme", FakeOrganization.WorkflowId(2).ToString("D") + ".xaml")));
        Assert.Contains("Poliçe İptal Süreci 2", File.ReadAllText(Path.Combine(runRoot, "elle-inceleme", "dizin.md")), StringComparison.Ordinal);
        Assert.Equal(1, Counts(runRoot)["manualReview"]);
    }

    [Fact]
    public async Task Drift_Separates_Real_Logic_Changes_From_Identifier_Differences()
    {
        FakeOrganization organization = new() { WorkflowCount = 6, Drifted = new HashSet<int> { 2 } };
        using TemporaryOutput output = new();

        (_, string runRoot, _) = await RunHarness.RunAsync(organization, output);

        Dictionary<string, int> counts = Counts(runRoot);
        Assert.Equal(3, counts["drift.pairsCompared"]);
        Assert.Equal(1, counts["drift.structureDiffers"]);
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        // Only the workflows whose running copy really differs, and only those the plan carries: every row is a
        // thing to go and check in CRM.
        IReadOnlyList<string> drifted = Assert.Single(plan.Rows("Sapma").Skip(1));
        Assert.Equal(FakeOrganization.WorkflowId(2).ToString("D"), drifted[1]);
    }

    [Fact]
    public async Task A_Failed_Xaml_Request_Is_Warned_And_The_Rest_Continue()
    {
        FakeOrganization organization = new() { WorkflowCount = 6, XamlMissing = new HashSet<int> { 3 } };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.Equal(ExitCode.Success, code);
        Assert.Equal(5, XamlEntry.ReadIndex(runRoot).Count);
        Assert.Equal(1, Counts(runRoot)["xaml.failed"]);
        Assert.Contains("tarayıcı dışa aktarımında okunamadı", console, StringComparison.Ordinal);
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
