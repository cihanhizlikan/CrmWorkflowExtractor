using System.Text.Json;
using Crm.Cli;
using Crm.Tests.Fakes;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class IrStageTests
{
    [Fact]
    public async Task Each_Designer_Definition_Gets_An_Ir_Document_With_Labels_From_Server_Metadata()
    {
        string xaml = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 6, NonDesigner = new HashSet<int> { 4 }, XamlFor = (_, _) => xaml }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        List<string> documents = [.. Directory.GetFiles(Path.Combine(runRoot, "ara-model"), "*.json").Select(path => Path.GetFileNameWithoutExtension(path)).Order(StringComparer.Ordinal)];
        Assert.Equal([FakeOrganization.WorkflowId(0).ToString("D"), FakeOrganization.WorkflowId(2).ToString("D")], documents);

        using JsonDocument ir = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "ara-model", FakeOrganization.WorkflowId(0).ToString("D") + ".json")));
        JsonElement condition = ir.RootElement.GetProperty("steps")[0];
        Assert.Equal("Condition", condition.GetProperty("kind").GetString());
        Assert.Equal("İptal Edildi", condition.GetProperty("branches")[0].GetProperty("predicate").GetProperty("values")[0].GetProperty("resolved").GetString());
        Assert.Equal("Poliçe İptal Süreci 0", ir.RootElement.GetProperty("identity").GetProperty("name").GetString());
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains("Yapı sıklığı", plan.Names);
        Assert.True(File.Exists(Path.Combine(runRoot, "raporlar", "hassas-degerler.md")));
    }

    [Fact]
    public async Task Unparseable_Xaml_Is_Counted_And_The_Others_Still_Get_Documents()
    {
        string good = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 4, XamlFor = (index, _) => index == 0 ? "<Activity><broken>" : good }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        Assert.Single(Directory.GetFiles(Path.Combine(runRoot, "ara-model")));
        Assert.Contains("ayrıştırılamadı", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rerunning_Over_Unchanged_Workflows_Produces_Byte_Identical_Ir_Apart_From_Run_Provenance()
    {
        string xaml = XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        using TemporaryOutput output = new();
        (_, string first, _) = await RunHarness.RunAsync(new FakeOrganization { WorkflowCount = 2, XamlFor = (_, _) => xaml }.Build(), output);
        (_, string second, _) = await RunHarness.RunAsync(new FakeOrganization { WorkflowCount = 2, XamlFor = (_, _) => xaml }.Build(), output);

        string file = FakeOrganization.WorkflowId(0).ToString("D") + ".json";
        Assert.Equal(WithoutExtractedAt(Path.Combine(first, "ara-model", file)), WithoutExtractedAt(Path.Combine(second, "ara-model", file)));
    }

    private static string WithoutExtractedAt(string path)
    {
        return string.Join("\n", File.ReadAllLines(path).Where(line => !line.Contains("\"extractedAtUtc\"", StringComparison.Ordinal)));
    }
}
