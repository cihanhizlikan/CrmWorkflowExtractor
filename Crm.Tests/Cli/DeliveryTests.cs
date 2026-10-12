using System.Xml.Linq;
using Crm.Cli;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// What is handed to the outsource partner is a subset of the run folder: the guide, three workbooks and the
/// original diagrams. The families, the combined models, the evidence, the report, the restricted findings, the
/// out-of-scope book and the run-authority workbook — which names security roles and how many people hold each —
/// stay with Enterprise Architecture. A delivered file that names one of those sends a reader looking for
/// something they do not have, which is worse than saying nothing — so none does.
/// </summary>
public sealed class DeliveryTests
{
    /// <summary>Everything left behind when the package is assembled. A cell may not mention any of it.</summary>
    private static readonly string[] NotDelivered =
    [
        "hassas-degerler", "kapsam-disi", "ham/", "ara-model", "elle-inceleme", "gunlukler", "aileler", "birlesik",
        "calistirma-yetkisi", "surec-madenciligi", ".json"
    ];

    private static readonly string[] Delivered =
    [
        "tasima-plani.xlsx", "veri-analizi.xlsx", "dis-sistemler.xlsx"
    ];

    /// <summary>
    /// The diagrams are delivered too, and are what an analyst reads most — so the same holds for every name and
    /// every line of documentation in them; nor may they carry the name of a CRM table. A diagram once told its reader
    /// to see a Markdown file nobody had, on every step it could not read.
    /// </summary>
    [Fact]
    public async Task No_Delivered_Diagram_Names_What_The_Analyst_Does_Not_Have()
    {
        // One workflow with a step the parser cannot read, because that step is where the dangling reference was.
        string xaml = Ir.XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        string unreadable = Ir.XamlWorkflowParserTests.Fixture("unknown-construct.xaml");
        FakeOrganization organization = new() { WorkflowCount = 8, XamlFor = (index, _) => index == 0 ? unreadable : xaml };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);
        string[] files = Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            XDocument diagram = XDocument.Load(file);
            IEnumerable<string> texts = diagram.Descendants().Attributes("name").Select(name => name.Value)
                .Concat(diagram.Descendants().Where(element => element.Name.LocalName is "documentation" or "text").Select(element => element.Value));
            foreach (string text in texts)
            {
                string? named = NotDelivered.Concat([".md", "ps_step"]).FirstOrDefault(left => text.Contains(left, StringComparison.OrdinalIgnoreCase));
                Assert.True(named is null, $"{Path.GetFileName(file)}: teslim edilmeyen '{named}' anılıyor: {text}");
            }
        }
    }

    /// <summary>
    /// The delivered workflow diagrams are exactly the plan's rows: a diagram without a row is work the plan says is
    /// not there, and a row without a diagram is a dead link.
    /// </summary>
    [Fact]
    public async Task The_Delivered_Workflow_Diagrams_Are_Exactly_The_Plan()
    {
        FakeOrganization organization = new() { WorkflowCount = 8, Managed = new HashSet<int> { 6 } };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);
        IReadOnlyList<IReadOnlyList<string>> rows = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx")).Rows("İş akışları");
        int file = rows[0].ToList().IndexOf("bpmn_dosyasi");
        List<string> planned = [.. rows.Skip(1).Select(row => row[file]).Order(StringComparer.Ordinal)];
        List<string> delivered = [.. Directory.GetFiles(Path.Combine(runRoot, "bpmn", "is-akislari"), "*.bpmn", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(runRoot, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal)];
        Assert.Equal(planned, delivered);
        Assert.Empty(Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task No_Delivered_Workbook_Names_A_File_The_Analyst_Does_Not_Have()
    {
        string xaml = Ir.XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeOrganization organization = new() { WorkflowCount = 8, XamlFor = (_, _) => xaml };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);
        foreach (string file in Delivered)
        {
            Workbook workbook = Workbook.Open(Path.Combine(runRoot, "raporlar", file));
            foreach (string sheet in workbook.Names)
            {
                foreach (IReadOnlyList<string> row in workbook.Rows(sheet))
                {
                    foreach (string cell in row)
                    {
                        string? named = NotDelivered.FirstOrDefault(left => cell.Contains(left, StringComparison.OrdinalIgnoreCase));
                        Assert.True(named is null, $"{file} → {sheet}: teslim edilmeyen '{named}' anılıyor: {cell}");
                    }
                }
            }
        }
    }
}
