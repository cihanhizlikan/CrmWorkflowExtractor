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
        "calistirma-yetkisi", ".json"
    ];

    private static readonly string[] Delivered =
    [
        "tasima-plani.xlsx", "veri-analizi.xlsx", "dis-sistemler.xlsx"
    ];

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
