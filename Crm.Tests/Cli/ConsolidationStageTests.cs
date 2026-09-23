using System.Text.Json;
using Crm.Cli;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class ConsolidationStageTests
{
    /// <summary>End to end: four copies of one workflow → one family → one combined BPMN, originals kept beside it.</summary>
    [Fact]
    public async Task A_Family_Of_Copies_Produces_One_Combined_Bpmn_And_The_Originals_Stay()
    {
        string xaml = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, XamlFor = (_, _) => xaml }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.True(code == ExitCode.Success, console);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length);
        string combined = Assert.Single(Directory.GetFiles(Path.Combine(runRoot, "birlesik"), "*.bpmn"));
        // Named for a reader: the family medoid, how many workflows it covers, and the tail of the cluster id.
        Assert.Contains("-combined-4-", Path.GetFileName(combined), StringComparison.Ordinal);
        Assert.DoesNotContain("cl_", Path.GetFileName(combined), StringComparison.Ordinal);
        Workbook families = Workbook.Open(Path.Combine(runRoot, "raporlar", "aileler.xlsx"));
        IReadOnlyList<IReadOnlyList<string>> rows = families.Rows("Birleştirme");
        Assert.Equal(4, rows.Count - 1);
        Assert.All(rows.Skip(1), row => Assert.Contains("birleştirildi", row));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        Assert.Equal(1, manifest.RootElement.GetProperty("stageCounts").GetProperty("consolidation.combined").GetInt32());
    }
}
