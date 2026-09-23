using Crm.Cli.Reports;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// A workbook nobody has to repair. Excel holds 32,767 characters in a cell and throws away a longer one while
/// telling the reader it "repaired" the file — a silent loss at the far end, found only because a maintainer
/// opened dis-sistemler.xlsx and Excel said so. XML cannot carry a control character at all, and a string scanned
/// out of a binary assembly may hold one.
/// </summary>
public sealed class CellLimitTests
{
    [Fact]
    public void A_Cell_Is_Never_Longer_Than_Excel_Will_Hold()
    {
        Sheet sheet = new("Deneme", "uzun");
        sheet.Row(new string('a', ExcelWorkbook.CellLimit * 2));

        Workbook workbook = Workbook.Read(ExcelWorkbook.Build([sheet]));

        string cell = workbook.Rows("Deneme")[1][0];
        Assert.Equal(ExcelWorkbook.CellLimit, cell.Length);
        Assert.EndsWith("…(kesildi)", cell, StringComparison.Ordinal);
    }

    /// <summary>A control character in a cell is not a repair but a crash: XML has no way to write one.</summary>
    [Fact]
    public void A_Control_Character_Never_Reaches_The_File()
    {
        Sheet sheet = new("Deneme", "ad");
        sheet.Row("Poliçe\u0001İptal\u0007");

        Workbook workbook = Workbook.Read(ExcelWorkbook.Build([sheet]));

        Assert.Equal("Poliçeİptal", workbook.Rows("Deneme")[1][0]);
    }

    /// <summary>
    /// The cap is not only Excel's: a cell holding four hundred workflow names is unreadable, and the number that
    /// matters is already its own column. The list says how many it left out rather than pretending to be whole.
    /// </summary>
    [Fact]
    public void A_Long_List_Says_How_Many_It_Left_Out()
    {
        IReadOnlyList<string> names = [.. Enumerable.Range(1, 100).Select(number => "Akış " + number)];

        string few = Sheet.List(names.Take(3));
        string many = Sheet.List(names, 40);

        Assert.Equal("Akış 1 | Akış 2 | Akış 3", few);
        Assert.StartsWith("Akış 1 | Akış 2 |", many, StringComparison.Ordinal);
        Assert.EndsWith(" | …ve 60 tane daha", many, StringComparison.Ordinal);
        Assert.DoesNotContain("Akış 41", many, StringComparison.Ordinal);
    }
}
