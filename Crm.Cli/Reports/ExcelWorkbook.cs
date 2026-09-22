using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Crm.Cli.Reports;

/// <summary>
/// An .xlsx workbook, written by hand. A workbook is a zip of XML parts, so it needs no library — and that matters
/// here: the tool takes no dependency it cannot read (§10), and the analysts get a file that opens in Excel with
/// the header frozen, a filter on every column and columns already wide enough to read, instead of a CSV that
/// arrives as one column or mangles Turkish characters in the import dialog.
///
/// <para>Deterministic: every entry carries the same fixed timestamp, so two runs over the same data are byte-identical.</para>
/// </summary>
public static class ExcelWorkbook
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Zip entries carry a timestamp; a fixed one keeps re-runs byte-identical.</summary>
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Build(IReadOnlyList<Sheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("A workbook needs at least one sheet.", nameof(sheets));
        }

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", ContentTypesPart(sheets.Count));
            Write(zip, "_rels/.rels", RootRelationships());
            Write(zip, "xl/workbook.xml", WorkbookPart(sheets));
            Write(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));
            Write(zip, "xl/styles.xml", StylesPart());
            for (int index = 0; index < sheets.Count; index++)
            {
                Write(zip, Invariant($"xl/worksheets/sheet{index + 1}.xml"), SheetPart(sheets[index]));
            }
        }
        return stream.ToArray();
    }

    private static void Write(ZipArchive zip, string path, XDocument document)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = Timestamp;
        using Stream content = entry.Open();
        using StreamWriter writer = new(content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(document.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        writer.Write('\n');
        writer.Write(document.ToString(SaveOptions.DisableFormatting));
    }

    private static XDocument ContentTypesPart(int sheetCount)
    {
        XElement types = new(ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        for (int index = 1; index <= sheetCount; index++)
        {
            types.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", Invariant($"/xl/worksheets/sheet{index}.xml")),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), types);
    }

    private static XDocument RootRelationships()
    {
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PackageRelationships + "Relationships",
                new XElement(PackageRelationships + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument WorkbookPart(IReadOnlyList<Sheet> sheets)
    {
        XElement list = new(Main + "sheets");
        for (int index = 0; index < sheets.Count; index++)
        {
            list.Add(new XElement(Main + "sheet",
                new XAttribute("name", sheets[index].Name),
                new XAttribute("sheetId", index + 1),
                new XAttribute(Relationships + "id", Invariant($"rId{index + 1}"))));
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Relationships), list));
    }

    private static XDocument WorkbookRelationships(int sheetCount)
    {
        XElement relationships = new(PackageRelationships + "Relationships");
        for (int index = 1; index <= sheetCount; index++)
        {
            relationships.Add(new XElement(PackageRelationships + "Relationship", new XAttribute("Id", Invariant($"rId{index}")),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", Invariant($"worksheets/sheet{index}.xml"))));
        }
        relationships.Add(new XElement(PackageRelationships + "Relationship", new XAttribute("Id", Invariant($"rId{sheetCount + 1}")),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), relationships);
    }

    /// <summary>Two styles: plain, and the bold header. Anything more is decoration the reader did not ask for.</summary>
    private static XDocument StylesPart()
    {
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Main + "styleSheet",
                new XElement(Main + "fonts", new XAttribute("count", 2),
                    new XElement(Main + "font", new XElement(Main + "sz", new XAttribute("val", 11)), new XElement(Main + "name", new XAttribute("val", "Calibri"))),
                    new XElement(Main + "font", new XElement(Main + "b"), new XElement(Main + "sz", new XAttribute("val", 11)), new XElement(Main + "name", new XAttribute("val", "Calibri")))),
                new XElement(Main + "fills", new XAttribute("count", 1), new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none")))),
                new XElement(Main + "borders", new XAttribute("count", 1), new XElement(Main + "border")),
                new XElement(Main + "cellStyleXfs", new XAttribute("count", 1), new XElement(Main + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0))),
                new XElement(Main + "cellXfs", new XAttribute("count", 2),
                    new XElement(Main + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("xfId", 0)),
                    new XElement(Main + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 1), new XAttribute("xfId", 0), new XAttribute("applyFont", 1)))));
    }

    private static XDocument SheetPart(Sheet sheet)
    {
        int columns = sheet.Headers.Count;
        string lastColumn = ColumnName(columns);
        int lastRow = sheet.Rows.Count + 1;

        XElement columnWidths = new(Main + "cols");
        for (int index = 0; index < columns; index++)
        {
            double width = Width(sheet, index);
            columnWidths.Add(new XElement(Main + "col", new XAttribute("min", index + 1), new XAttribute("max", index + 1),
                new XAttribute("width", width.ToString("0.##", CultureInfo.InvariantCulture)), new XAttribute("customWidth", 1)));
        }

        XElement data = new(Main + "sheetData", HeaderRow(sheet));
        for (int index = 0; index < sheet.Rows.Count; index++)
        {
            data.Add(DataRow(sheet.Rows[index], index + 2));
        }

        XElement worksheet = new(Main + "worksheet",
            new XElement(Main + "dimension", new XAttribute("ref", Invariant($"A1:{lastColumn}{lastRow}"))),
            new XElement(Main + "sheetViews",
                new XElement(Main + "sheetView", new XAttribute("workbookViewId", 0),
                    new XElement(Main + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"),
                        new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            new XElement(Main + "sheetFormatPr", new XAttribute("defaultRowHeight", 15)),
            columnWidths,
            data,
            new XElement(Main + "autoFilter", new XAttribute("ref", Invariant($"A1:{lastColumn}{lastRow}"))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet);
    }

    private static XElement HeaderRow(Sheet sheet)
    {
        XElement row = new(Main + "row", new XAttribute("r", 1));
        for (int index = 0; index < sheet.Headers.Count; index++)
        {
            row.Add(TextCell(Invariant($"{ColumnName(index + 1)}1"), sheet.Headers[index], header: true));
        }
        return row;
    }

    private static XElement DataRow(IReadOnlyList<object?> values, int rowNumber)
    {
        XElement row = new(Main + "row", new XAttribute("r", rowNumber));
        for (int index = 0; index < values.Count; index++)
        {
            object? cell = Sheet.Cell(values[index]);
            string reference = Invariant($"{ColumnName(index + 1)}{rowNumber}");
            if (cell is null)
            {
                continue;
            }
            if (cell is double or int or long)
            {
                row.Add(new XElement(Main + "c", new XAttribute("r", reference),
                    new XElement(Main + "v", Convert.ToString(cell, CultureInfo.InvariantCulture))));
                continue;
            }
            string text = cell.ToString() ?? "";
            if (text.Length > 0)
            {
                row.Add(TextCell(reference, text, header: false));
            }
        }
        return row;
    }

    /// <summary>Inline strings: a shared-string table would save bytes and cost every reader the ability to diff the file.</summary>
    private static XElement TextCell(string reference, string text, bool header)
    {
        XElement cell = new(Main + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"),
            new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        if (header)
        {
            cell.Add(new XAttribute("s", 1));
        }
        return cell;
    }

    /// <summary>Wide enough to read the longest value, capped so one long argument does not push everything off-screen.</summary>
    private static double Width(Sheet sheet, int column)
    {
        int longest = sheet.Headers[column].Length;
        foreach (IReadOnlyList<object?> row in sheet.Rows)
        {
            if (column < row.Count && Sheet.Cell(row[column])?.ToString() is string text && text.Length > longest)
            {
                longest = text.Length;
            }
        }
        return Math.Clamp(longest + 2, 10, 60);
    }

    private static string ColumnName(int index)
    {
        StringBuilder name = new();
        int remaining = index;
        while (remaining > 0)
        {
            int letter = (remaining - 1) % 26;
            name.Insert(0, (char)('A' + letter));
            remaining = (remaining - 1) / 26;
        }
        return name.ToString();
    }

    private static string Invariant(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }
}
