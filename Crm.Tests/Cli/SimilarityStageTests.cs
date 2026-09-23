using Crm.Cli;
using Crm.Tests.Fakes;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class SimilarityStageTests
{
    [Fact]
    public async Task The_Family_Workbook_Has_One_Row_Per_Workflow_And_An_Empty_Decision_Column()
    {
        string update = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        string custom = XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, XamlFor = (index, _) => index < 6 ? update : custom }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        Workbook families = Workbook.Open(Path.Combine(runRoot, "raporlar", "aileler.xlsx"));
        Assert.Equal(["Nasıl okunur", "Aileler", "Birleştirme", "Çiftler", "Taslaklar", "Ürünle gelenler"], families.Names);
        Assert.Equal(
            ["aile_id", "aile_buyuklugu", "is_akisi", "is_akisi_id", "birincil_varlik", "kategori", "durum",
             "baslangica_benzerlik", "baslangic_noktasi", "zayif_tutarlilik", "adi_deneme_gibi", "son_kayitli_calisma", "karar"],
            families.Headers("Aileler"));

        IReadOnlyList<IReadOnlyList<string>> rows = families.Rows("Aileler");
        Assert.Equal(4, rows.Count - 1);
        // The decision column is the architect's to fill in, so it is written empty — and an empty trailing cell is
        // simply absent from the row, which is what a reader of the file sees as a blank.
        Assert.All(rows.Skip(1), row => Assert.True(row.Count < 13 || row[12].Length == 0));
        Assert.Contains(rows, row => row.Contains("Poliçe İptal Süreci 0"));
        Assert.True(families.HasFrozenHeaderAndFilter("Aileler"));
        Assert.True(File.Exists(Path.Combine(runRoot, "aileler", "aileler.json")));
    }
}
