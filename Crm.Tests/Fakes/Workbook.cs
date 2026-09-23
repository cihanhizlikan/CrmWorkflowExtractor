using System.IO.Compression;
using System.Xml.Linq;

namespace Crm.Tests.Fakes;

/// <summary>
/// Reads an .xlsx back without a library, so the tests check the file Excel will actually open rather than the
/// object that produced it. Only what the assertions need: sheet names, cell text, and the frozen pane / filter.
/// </summary>
internal sealed class Workbook
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly Dictionary<string, XDocument> _sheets = new(StringComparer.Ordinal);

    private Workbook(IReadOnlyList<string> names, IReadOnlyList<XDocument> sheets)
    {
        Names = names;
        for (int index = 0; index < names.Count; index++)
        {
            _sheets[names[index]] = sheets[index];
        }
    }

    /// <summary>Sheet names, in workbook order.</summary>
    public IReadOnlyList<string> Names { get; }

    public static Workbook Open(string path)
    {
        return Read(File.ReadAllBytes(path));
    }

    /// <summary>The same workbook before it reaches a disk: what the writer produced, byte for byte.</summary>
    public static Workbook Read(byte[] bytes)
    {
        using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);
        XDocument workbook = Read(zip, "xl/workbook.xml");
        List<string> names = [.. workbook.Descendants(Main + "sheet").Select(sheet => sheet.Attribute("name")!.Value)];
        List<XDocument> sheets = [.. Enumerable.Range(1, names.Count).Select(index => Read(zip, $"xl/worksheets/sheet{index}.xml"))];
        return new Workbook(names, sheets);
    }

    /// <summary>Every row of a sheet as text, header row first; an empty cell is an empty string.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows(string sheetName)
    {
        List<IReadOnlyList<string>> rows = [];
        foreach (XElement row in _sheets[sheetName].Descendants(Main + "row"))
        {
            List<string> cells = [];
            foreach (XElement cell in row.Elements(Main + "c"))
            {
                cells.Add(cell.Attribute("t")?.Value == "inlineStr"
                    ? cell.Element(Main + "is")?.Element(Main + "t")?.Value ?? ""
                    : cell.Element(Main + "v")?.Value ?? "");
            }
            rows.Add(cells);
        }
        return rows;
    }

    public IReadOnlyList<string> Headers(string sheetName)
    {
        return Rows(sheetName)[0];
    }

    public bool HasFrozenHeaderAndFilter(string sheetName)
    {
        XDocument sheet = _sheets[sheetName];
        XElement? pane = sheet.Descendants(Main + "pane").FirstOrDefault();
        return pane?.Attribute("state")?.Value == "frozen" && pane.Attribute("ySplit")?.Value == "1"
            && sheet.Descendants(Main + "autoFilter").Any();
    }

    /// <summary>True when the cell was written as a number, not as text — that is what makes Excel sort it as one.</summary>
    public bool IsNumeric(string sheetName, int row, int column)
    {
        XElement cell = _sheets[sheetName].Descendants(Main + "row").ElementAt(row).Elements(Main + "c").ElementAt(column);
        return cell.Attribute("t") is null && cell.Element(Main + "v") is not null;
    }

    private static XDocument Read(ZipArchive zip, string path)
    {
        using Stream stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }
}
