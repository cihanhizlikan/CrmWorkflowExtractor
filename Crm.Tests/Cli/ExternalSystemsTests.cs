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

    private static ParseResult Parse(Guid id)
    {
        return new XamlWorkflowParser(OptionLabels.Empty).Parse(id, XamlWorkflowParserTests.Fixture("child-and-custom.xaml"));
    }

    private static WorkflowIr Ir(Guid id, string name)
    {
        ParseResult result = Parse(id);
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
    }

    /// <summary>
    /// The reason addresses are read from the literals and not from the arguments captured on steps: on the real
    /// data every address sat somewhere else — a variable, an expression, a construct the parser could not read —
    /// and the delivered page came out empty while the restricted report was reporting embedded addresses.
    /// </summary>
    [Fact]
    public void An_Address_Anywhere_In_The_Definition_Is_Found_Not_Only_In_A_Captured_Argument()
    {
        IReadOnlyList<XamlLiteral> literals =
        [
            new XamlLiteral("Sequence/Assign", "https://nova.local:8443/api/sign?x=1"),
            new XamlLiteral("", "http://schemas.microsoft.com/netfx/2009/xaml/activities"),
            new XamlLiteral("Sequence/Variables", @"\\dosya01\paylasim\cikti"),
            new XamlLiteral("Sequence/If", "ftp://sistem:parola@ftp.local/out")
        ];

        IReadOnlyList<ExternalAddress> found = ExternalSystems.Find("Poliçe İptal", literals);

        // The XAML's own schema namespaces are not addresses.
        Assert.Equal(3, found.Count);
        Assert.All(found, address => Assert.Equal("Poliçe İptal", address.Workflow));
        Assert.Contains(found, address => address.Host == "nova.local");
        Assert.Contains(found, address => address.Host == "dosya01");
        // The page is delivered; a password written into an address is not.
        ExternalAddress ftp = Assert.Single(found, address => address.Host == "ftp.local");
        Assert.Equal("ftp://***@ftp.local/out", ftp.Address);
        Assert.DoesNotContain("parola", ftp.Address, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Address_In_A_Custom_Activity_Argument_Is_Still_Found()
    {
        IReadOnlyList<ExternalAddress> found = ExternalSystems.Find("Poliçe İptal", Parse(First).Literals);

        Assert.Contains(found, address => address.Address.StartsWith("https://", StringComparison.Ordinal));
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
        Assert.Equal(["etkinlik", "cagiran_is_akisi_sayisi", "cagiran_is_akislari", "derleme"],
            workbook.Headers("Dış bağımlılıklar"));
        Assert.Contains(workbook.Rows("Dış bağımlılıklar"), row => row.Contains("NotifyPolicyService"));
        Assert.Equal(["sunucu", "adres", "is_akisi"], workbook.Headers("Adresler"));
        Assert.Contains(workbook.Rows("Adresler"), row => row.Count > 1 && row[1].StartsWith("https://", StringComparison.Ordinal));
        // The guide has to say what the page cannot see, or a reader takes an empty page for "calls nothing".
        Assert.Contains(workbook.Rows("Nasıl okunur"), row => row.Any(cell => cell.Contains("Görülemeyen", StringComparison.Ordinal)));
        Assert.Contains(workbook.Rows("Nasıl okunur"), row => row.Any(cell => cell.Contains("demek DEĞİLDİR", StringComparison.Ordinal)));
    }
}
