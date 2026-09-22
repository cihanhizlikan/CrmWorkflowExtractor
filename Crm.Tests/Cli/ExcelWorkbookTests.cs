using System.IO.Compression;
using Crm.Cli.Reports;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The workbook is written by hand, so these tests check the parts Excel refuses a file over: the content types,
/// the relationships, one worksheet part per sheet — and the things that make it usable at all.
/// </summary>
public sealed class ExcelWorkbookTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "crm-extract-tests", Guid.NewGuid().ToString("N") + ".xlsx");

    private static Sheet Sample()
    {
        Sheet sheet = new("Taşıma planı", "öncelik", "is_akisi", "adi_deneme_gibi", "adim");
        sheet.Row(1, "Poliçe İptal Süreci", false, 12);
        sheet.Row(4, "DRAFT_ÖRNEK; noktalı virgüllü", true, 3);
        return sheet;
    }

    private Workbook Write(params Sheet[] sheets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllBytes(_file, ExcelWorkbook.Build(sheets));
        return Workbook.Open(_file);
    }

    public void Dispose()
    {
        File.Delete(_file);
    }

    [Fact]
    public void A_Workbook_Carries_Every_Part_Excel_Requires()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllBytes(_file, ExcelWorkbook.Build([Sample(), new Sheet("Kullanım", "is_akisi")]));

        using ZipArchive zip = ZipFile.OpenRead(_file);
        List<string> parts = [.. zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal)];

        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml",
             "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml"],
            parts);
    }

    [Fact]
    public void Turkish_Text_Survives_And_Needs_No_Import_Dialog()
    {
        Workbook workbook = Write(Sample());

        Assert.Equal("Taşıma planı", Assert.Single(workbook.Names));
        Assert.Equal(["öncelik", "is_akisi", "adi_deneme_gibi", "adim"], workbook.Headers("Taşıma planı"));
        // A semicolon inside a value is what made the CSV ambiguous; here it is just text.
        Assert.Contains(workbook.Rows("Taşıma planı"), row => row.Contains("DRAFT_ÖRNEK; noktalı virgüllü"));
    }

    [Fact]
    public void Numbers_Stay_Numbers_And_Booleans_Read_As_Turkish()
    {
        Workbook workbook = Write(Sample());

        Assert.True(workbook.IsNumeric("Taşıma planı", 1, 0));
        Assert.True(workbook.IsNumeric("Taşıma planı", 1, 3));
        Assert.False(workbook.IsNumeric("Taşıma planı", 1, 1));
        Assert.Equal("hayır", workbook.Rows("Taşıma planı")[1][2]);
        Assert.Equal("evet", workbook.Rows("Taşıma planı")[2][2]);
    }

    [Fact]
    public void The_Header_Is_Frozen_And_Every_Column_Filterable()
    {
        Assert.True(Write(Sample()).HasFrozenHeaderAndFilter("Taşıma planı"));
    }

    /// <summary>A zip carries timestamps; the same data twice must still be the same file, as every other output is.</summary>
    [Fact]
    public void The_Same_Data_Twice_Is_Byte_Identical()
    {
        Assert.Equal(ExcelWorkbook.Build([Sample()]), ExcelWorkbook.Build([Sample()]));
    }

    [Fact]
    public void A_Sheet_Name_Too_Long_For_Excel_Is_Cut_Rather_Than_Refused()
    {
        Sheet sheet = new(new string('ş', 40), "a");

        Assert.Equal(Sheet.MaxNameLength, sheet.Name.Length);
    }
}
