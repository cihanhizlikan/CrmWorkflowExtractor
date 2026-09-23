using Crm.Cli;
using Crm.Cli.Reports;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Tests.Fakes;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// A CRM workflow reaches outside only through a custom activity, so this report inverts them: per activity, who
/// calls it. What the activity does inside its own assembly is not in the XAML and is never guessed at here.
/// </summary>
public sealed class ExternalSystemsTests
{
    private static readonly Guid First = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private static WorkflowIr Ir(Guid id, string name)
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(id, XamlWorkflowParserTests.Fixture("child-and-custom.xaml"));
        return new WorkflowIr(
            new WorkflowIdentity(id, name, null, ProcessLabels.CategoryWorkflow, "Tanım", "new_policy", ProcessLabels.ModeBackground,
                "Kuruluş", ProcessLabels.StateActivated, false, false, null, null, null, 1, true),
            new WorkflowTrigger(true, false, [], null, null, null, "Sahip", false),
            result.Steps, result.Dependencies, result.DataTouched, result.Warnings,
            new IrProvenance("ham/xaml/x.xaml", "abc", "2026-09-23T00:00:00Z", "test"));
    }

    [Fact]
    public void A_Custom_Activity_Is_Listed_Once_With_Every_Workflow_That_Calls_It()
    {
        IReadOnlyList<ExternalDependency> dependencies = ExternalSystems.Dependencies([Ir(First, "Poliçe İptal"), Ir(Second, "Hasar Onay")]);

        ExternalDependency dependency = Assert.Single(dependencies);
        Assert.Equal("NotifyPolicyService", dependency.Activity);
        Assert.Equal(["Hasar Onay", "Poliçe İptal"], dependency.Workflows);
        Assert.Contains(dependency.Addresses, address => address.StartsWith("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Address_The_Workflow_Passes_Is_Reported_With_Where_It_Was_Found()
    {
        ExternalAddress address = Assert.Single(ExternalSystems.Addresses([Ir(First, "Poliçe İptal")]));

        Assert.StartsWith("https://", address.Address, StringComparison.Ordinal);
        Assert.Equal("Poliçe İptal", address.Workflow);
        Assert.False(string.IsNullOrEmpty(address.Argument));
    }

    /// <summary>The page has to say what it cannot see, or a reader will take an empty column for "calls nothing".</summary>
    [Fact]
    public void The_Page_States_What_Cannot_Be_Seen()
    {
        string markdown = ExternalSystems.Markdown([Ir(First, "Poliçe İptal")]);

        Assert.Contains("kendi derlemesi içinde ne yaptığı", markdown, StringComparison.Ordinal);
        Assert.Contains("| NotifyPolicyService |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Run_Writes_The_External_Systems_Workbook()
    {
        string xaml = XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, XamlFor = (_, _) => xaml }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.True(code == ExitCode.Success, console);
        Workbook workbook = Workbook.Open(Path.Combine(runRoot, "raporlar", "dis-sistemler.xlsx"));
        Assert.Equal(["Nasıl okunur", "Dış bağımlılıklar", "Adresler"], workbook.Names);
        Assert.Equal(["etkinlik", "derleme", "cagiran_is_akisi_sayisi", "gecen_adresler", "cagiran_is_akislari"],
            workbook.Headers("Dış bağımlılıklar"));
        Assert.Contains(workbook.Rows("Dış bağımlılıklar"), row => row.Contains("NotifyPolicyService"));
        Assert.Contains(workbook.Rows("Nasıl okunur"), row => row.Any(cell => cell.Contains("Görülemeyen", StringComparison.Ordinal)));
    }
}
