using System.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// One thing in the document: a paragraph in one of the department's styles, a table, a picture or a page break.
/// A part knows how to write itself, so the document is assembled as a list and nothing has to be laid out by
/// hand — Word does the pagination, which is the whole reason for producing one of these rather than a PDF.
/// </summary>
public abstract record DocumentPart
{
    public abstract string ToXml(byte[]? logo);

    /// <summary>A run of text inside a paragraph, optionally bold.</summary>
    protected static string Run(string text, bool bold = false)
    {
        StringBuilder run = new();
        run.Append("<w:r>");
        if (bold)
        {
            run.Append("<w:rPr><w:b/></w:rPr>");
        }
        // xml:space matters: a caption ending in a space loses it otherwise, and so does an indent.
        run.Append("<w:t xml:space=\"preserve\">").Append(WordDocument.Text(text)).Append("</w:t></w:r>");
        return run.ToString();
    }
}

/// <summary>A paragraph in a named style. <paramref name="Lead"/> is set in bold before the rest, for a "Label: value" line.</summary>
public sealed record Paragraph(string Style, string Text, string? Lead = null) : DocumentPart
{
    public override string ToXml(byte[]? logo)
    {
        StringBuilder paragraph = new();
        paragraph.Append("<w:p><w:pPr><w:pStyle w:val=\"").Append(Style).Append("\"/></w:pPr>");
        if (Lead is string lead)
        {
            paragraph.Append(Run(lead, bold: true));
        }
        if (Text.Length > 0)
        {
            paragraph.Append(Run(Text));
        }
        paragraph.Append("</w:p>");
        return paragraph.ToString();
    }
}

/// <summary>A table, in the department's own look: a bold centred header row and a hairline grid.</summary>
public sealed record Table(IReadOnlyList<DocumentRow> Rows, IReadOnlyList<int> Widths) : DocumentPart
{
    public override string ToXml(byte[]? logo)
    {
        StringBuilder table = new();
        table.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders>");
        foreach (string edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
        {
            table.Append("<w:").Append(edge).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/>");
        }
        table.Append("</w:tblBorders></w:tblPr><w:tblGrid>");
        foreach (int width in Widths)
        {
            table.Append("<w:gridCol w:w=\"").Append(width.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\"/>");
        }
        table.Append("</w:tblGrid>");
        foreach (DocumentRow row in Rows)
        {
            table.Append("<w:tr>");
            if (row.Header)
            {
                // Repeated at the top of every page a long table runs onto, as the department's own tables are.
                table.Append("<w:trPr><w:tblHeader/></w:trPr>");
            }
            for (int index = 0; index < row.Cells.Count; index++)
            {
                table.Append("<w:tc><w:tcPr><w:tcW w:w=\"")
                    .Append((index < Widths.Count ? Widths[index] : 2000).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append("\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
                table.Append(new Paragraph(row.Header ? "DocTableHeader" : "DocTableText", row.Cells[index]).ToXml(null));
                table.Append("</w:tc>");
            }
            table.Append("</w:tr>");
        }
        table.Append("</w:tbl>");
        // Word needs a paragraph after a table, or a table that ends a section is not editable below.
        table.Append("<w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:p>");
        return table.ToString();
    }
}

public sealed record PageBreak : DocumentPart
{
    public override string ToXml(byte[]? logo)
    {
        return "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>";
    }
}

/// <summary>
/// The organisation's logo on the cover. Sized from the image's own pixels so it is never stretched; EMUs are
/// English Metric Units, 9525 to a pixel at 96 dots per inch, which is what Word measures a picture in.
/// </summary>
public sealed record Logo(int WidthPixels, int HeightPixels, int DrawnWidth) : DocumentPart
{
    private const int PerPixel = 9525;

    public override string ToXml(byte[]? logo)
    {
        if (logo is null || WidthPixels <= 0 || HeightPixels <= 0)
        {
            return "";
        }
        long width = (long)DrawnWidth * PerPixel;
        long height = width * HeightPixels / WidthPixels;
        return "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>"
            + WordDocument.Invariant($"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{width}\" cy=\"{height}\"/>")
            + "<wp:docPr id=\"1\" name=\"Logo\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + "<pic:pic><pic:nvPicPr><pic:cNvPr id=\"1\" name=\"logo.png\"/><pic:cNvPicPr/></pic:nvPicPr>"
            + "<pic:blipFill><a:blip r:embed=\"rId2\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + WordDocument.Invariant($"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{width}\" cy=\"{height}\"/></a:xfrm>")
            + "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>"
            + "</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }
}
