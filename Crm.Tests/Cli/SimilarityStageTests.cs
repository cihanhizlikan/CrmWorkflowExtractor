using System.Text;
using Crm.Cli;
using Crm.Tests.Fakes;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class SimilarityStageTests
{
    [Fact]
    public async Task Clusters_Csv_Opens_In_Turkish_Excel_With_One_Row_Per_Workflow_And_An_Empty_Decision_Column()
    {
        string update = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        string custom = XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, XamlFor = (index, _) => index < 6 ? update : custom }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        byte[] bytes = File.ReadAllBytes(Path.Combine(runRoot, "clusters", "clusters.csv"));
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        string[] lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("cluster_id;cluster_size;workflow_name;workflow_id;primary_entity;category;state;score_to_medoid;is_medoid;low_cohesion;decision", lines[0]);
        Assert.Equal(4, lines.Length - 1);
        Assert.All(lines.Skip(1), line => Assert.EndsWith(";", line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Poliçe İptal Süreci 0", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(runRoot, "clusters", "clusters.json")));
        Assert.True(File.Exists(Path.Combine(runRoot, "clusters", "pairs.csv")));
    }
}
