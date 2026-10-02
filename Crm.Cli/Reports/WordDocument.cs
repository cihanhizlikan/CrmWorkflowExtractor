using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Crm.Cli.Reports;

/// <summary>One row of a table in the document: its cells, and whether it is the header row.</summary>
public sealed record DocumentRow(IReadOnlyList<string> Cells, bool Header = false);

/// <summary>
/// A .docx written by hand — a zip of XML, like <see cref="ExcelWorkbook"/>, so the locked-down host needs no
/// dependency to produce one.
///
/// <para>
/// The shape and the styling follow the department's own standard document (AHE-BT-EY-STD, supplied 2026-10-02):
/// A4 with its margins, Arial 11 justified for body text, a 14-point heading in its navy, tables in Arial 10 with
/// a bold centred header row, a cover carrying the document's own particulars, a revision table and a contents
/// list. An analyst opening this beside the department's other standards should not be able to tell it was
/// produced by a tool.
/// </para>
/// </summary>
public static class WordDocument
{
    /// <summary>The heading navy of the department's standard: <c>#333399</c>.</summary>
    public const string HeadingColour = "333399";

    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static byte[] Build(IReadOnlyList<DocumentPart> parts, byte[]? logo)
    {
        using MemoryStream file = new();
        using (ZipArchive zip = new(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", ContentTypes(logo is not null));
            Write(zip, "_rels/.rels", Relationships());
            Write(zip, "word/_rels/document.xml.rels", DocumentRelationships(logo is not null));
            Write(zip, "word/styles.xml", Styles());
            Write(zip, "word/document.xml", Document(parts, logo));
            if (logo is byte[] image)
            {
                ZipArchiveEntry entry = zip.CreateEntry("word/media/logo.png", CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write(image);
            }
        }
        return file.ToArray();
    }

    private static void Write(ZipArchive zip, string path, string xml)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(Utf8.GetBytes(xml));
    }

    private static string ContentTypes(bool logo)
    {
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">"""
            + """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>"""
            + """<Default Extension="xml" ContentType="application/xml"/>"""
            + (logo ? """<Default Extension="png" ContentType="image/png"/>""" : "")
            + """<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>"""
            + """<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>"""
            + "</Types>";
    }

    private static string Relationships()
    {
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
            + """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>"""
            + "</Relationships>";
    }

    private static string DocumentRelationships(bool logo)
    {
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
            + """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>"""
            + (logo ? """<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/logo.png"/>""" : "")
            + "</Relationships>";
    }

    /// <summary>
    /// The styles the department's document defines, as far as this one uses them. Arial 11 justified is its body;
    /// headings are bold, the first level 14-point and navy; table text is Arial 10.
    /// </summary>
    private static string Styles()
    {
        StringBuilder styles = new();
        styles.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""");
        styles.Append("""<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/><w:sz w:val="22"/><w:lang w:val="tr-TR"/></w:rPr></w:rPrDefault>""");
        styles.Append("""<w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="259" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>""");
        styles.Append(Style("Normal", "Normal", new Look(Justify: "both", Size: 22)));
        styles.Append(Style("Heading1", "heading 1", new Look(KeepNext: true, Before: 320, After: 160, Outline: 0, Bold: true, Colour: HeadingColour, Size: 28)));
        styles.Append(Style("Heading2", "heading 2", new Look(KeepNext: true, Before: 240, After: 120, Outline: 1, Bold: true, Colour: HeadingColour, Size: 24)));
        styles.Append(Style("Heading3", "heading 3", new Look(KeepNext: true, Before: 200, After: 100, Outline: 2, Bold: true, Size: 22)));
        styles.Append(Style("CoverMainTitle", "CoverMainTitle", new Look(Before: 240, After: 240, Justify: "center", Font: "Verdana", Colour: "0000FF", Size: 40)));
        styles.Append(Style("CoverDocInfo", "CoverDocInfo", new Look(After: 60, Bold: true, Size: 22)));
        styles.Append(Style("DocTableHeader", "DocTableHeader", new Look(After: 0, Justify: "center", Bold: true, Size: 20)));
        styles.Append(Style("DocTableText", "DocTableText", new Look(After: 0, Colour: "000000", Size: 20)));
        styles.Append(Style("NormalBullet", "NormalBullet", new Look(After: 60, Indent: 454, Justify: "both", Size: 22)));
        styles.Append("</w:styles>");
        return styles.ToString();
    }

    /// <summary>What one style looks like, as the few properties this document actually varies.</summary>
    private sealed record Look(bool KeepNext = false, int? Before = null, int? After = null, int? Indent = null,
        string? Justify = null, int? Outline = null, string? Font = null, bool Bold = false, string? Colour = null, int? Size = null);

    /// <summary>
    /// One style, with its properties in the order the schema demands.
    ///
    /// <para>
    /// THE ORDER IS NOT COSMETIC. WordprocessingML fixes the sequence of the children of <c>w:pPr</c> and
    /// <c>w:rPr</c>, and a file that puts <c>w:outlineLvl</c> before <c>w:spacing</c>, or <c>w:sz</c> before
    /// <c>w:color</c>, is one Word declares unreadable and "repairs" — which is to say throws away. It is written
    /// here, once, rather than at each call site, so that no style can be added in the wrong order.
    /// </para>
    /// </summary>
    private static string Style(string id, string name, Look look)
    {
        StringBuilder paragraph = new();
        if (look.KeepNext)
        {
            paragraph.Append("<w:keepNext/>");
        }
        if (look.Before is int before || look.After is int)
        {
            before = look.Before ?? 0;
            paragraph.Append(Invariant($"""<w:spacing w:before="{before}" w:after="{look.After ?? 120}"/>"""));
        }
        if (look.Indent is int indent)
        {
            paragraph.Append(Invariant($"""<w:ind w:left="{indent}" w:hanging="227"/>"""));
        }
        if (look.Justify is string justify)
        {
            paragraph.Append(Invariant($"""<w:jc w:val="{justify}"/>"""));
        }
        if (look.Outline is int outline)
        {
            paragraph.Append(Invariant($"""<w:outlineLvl w:val="{outline}"/>"""));
        }

        StringBuilder run = new();
        if (look.Font is string font)
        {
            run.Append(Invariant($"""<w:rFonts w:ascii="{font}" w:hAnsi="{font}"/>"""));
        }
        if (look.Bold)
        {
            run.Append("<w:b/>");
        }
        if (look.Colour is string colour)
        {
            run.Append(Invariant($"""<w:color w:val="{colour}"/>"""));
        }
        if (look.Size is int size)
        {
            run.Append(Invariant($"""<w:sz w:val="{size}"/>"""));
        }
        return $"""<w:style w:type="paragraph" w:styleId="{id}"><w:name w:val="{name}"/><w:qFormat/><w:pPr>{paragraph}</w:pPr><w:rPr>{run}</w:rPr></w:style>""";
    }

    private static string Document(IReadOnlyList<DocumentPart> parts, byte[]? logo)
    {
        StringBuilder body = new();
        body.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" """);
        body.Append("""xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """);
        body.Append("""xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" """);
        body.Append("""xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" """);
        body.Append("""xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>""");
        foreach (DocumentPart part in parts)
        {
            body.Append(part.ToXml(logo));
        }
        // A4 with the department's own margins, so a page of this sits in the same binder as one of theirs.
        body.Append("""<w:sectPr><w:pgSz w:w="11907" w:h="16840" w:code="9"/>""");
        body.Append("""<w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>""");
        body.Append("</w:body></w:document>");
        return body.ToString();
    }

    /// <summary>
    /// XML text. A .docx is XML, so the five characters have to go — and so do the control characters, which a
    /// literal scanned out of a CRM definition can carry and which no XML document may hold at all.
    /// </summary>
    public static string Text(string text)
    {
        StringBuilder clean = new(text.Length);
        foreach (char letter in text)
        {
            if (char.IsControl(letter) && letter is not ('\t' or '\n'))
            {
                continue;
            }
            clean.Append(letter switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => letter.ToString()
            });
        }
        return clean.ToString();
    }

    internal static string Invariant(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }
}
