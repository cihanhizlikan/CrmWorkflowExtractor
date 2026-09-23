using Crm.Cli;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// What is handed to the analysts is a subset of the run folder: the four workbooks, the diagrams, the combined
/// models and the report. The evidence, the restricted findings and the out-of-scope book stay with Enterprise
/// Architecture. A delivered file that names one of those sends a reader looking for something they do not have,
/// which is worse than saying nothing — so no delivered file names one.
/// </summary>
public sealed class DeliveryTests
{
    /// <summary>Everything left behind when the package is assembled. A cell may not mention any of it.</summary>
    private static readonly string[] NotDelivered =
    [
        "hassas-degerler", "kapsam-disi", "ham/", "ara-model", "elle-inceleme", "gunlukler", "aileler/", ".json"
    ];

    private static readonly string[] Delivered =
    [
        "tasima-plani.xlsx", "aileler.xlsx", "veri-analizi.xlsx", "dis-sistemler.xlsx"
    ];

    [Fact]
    public async Task No_Delivered_Workbook_Names_A_File_The_Analyst_Does_Not_Have()
    {
        string xaml = Ir.XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, XamlFor = (_, _) => xaml }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

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
