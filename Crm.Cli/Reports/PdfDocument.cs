using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// A PDF written by hand, the way <see cref="ExcelWorkbook"/> writes .xlsx by hand: a PDF is a list of numbered
/// objects and a table of where each one starts, so it needs no dependency. It lays text out in one column, draws
/// the few shapes the page furniture needs, and breaks pages itself — enough for a guide, and nothing more.
///
/// <para>
/// Text is set in an embedded TrueType font through an Identity-H encoding, which means the page carries glyph
/// numbers rather than characters. That is what makes ğ, ı and ş come out right on a reader that has never seen a
/// Turkish document; a <c>ToUnicode</c> map is written beside it so the text can still be searched and copied.
/// </para>
/// </summary>
public sealed class PdfDocument(TrueTypeFont font, string title)
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Left = 56;
    private const double Right = PageWidth - 56;
    private const double Top = PageHeight - 64;
    private const double Bottom = 64;
    private const double Leading = 1.45;

    /// <summary>A circle drawn as four Bézier arcs needs this much of the radius as its control-point offset.</summary>
    private const double Kappa = 0.5523;

    /// <summary>How wide the logo is drawn. The file is 436 pixels across, so this is about 240 dots to the inch.</summary>
    private const double LogoWidth = 130;

    // The organisation's own colours and page furniture. The blue and the green are the two colours of its logo
    // itself (#005C9C and #6CB644), so the page and the mark on it agree; the site's furniture is here too — white
    // cards with a pale rule on a near-white ground, and the green kept for the one thing it wants pressed. Here
    // the green is used the same way: only on what the reader is meant to act on.
    private const string Blue = "0 0.361 0.612";
    private const string DeepBlue = "0 0.29 0.49";
    private const string Green = "0.424 0.714 0.267";
    private const string Ink = "0.094 0.082 0.078";
    private const string Quiet = "0.38 0.424 0.439";
    private const string Tint = "0.929 0.957 0.98";
    private const string Edge = "0.859 0.882 0.894";
    private const string Paper = "1 1 1";

    private readonly List<StringBuilder> _pages = [];
    private readonly SortedSet<int> _used = [];
    private readonly Dictionary<int, char> _letters = [];
    private StringBuilder _page = new();
    private PngImage? _logo;
    private double _y = Top;

    public double Width
    {
        get { return Right - Left; }
    }

    /// <summary>
    /// The band across the top of the first page: the only place the guide raises its voice. The logo sits on a
    /// white card inside it — the mark is drawn for a light ground, and a card is how the site carries it too.
    /// </summary>
    public void Banner(string heading, string subtitle, string stamp, PngImage? logo)
    {
        Fill(DeepBlue, 0, PageHeight - 190, PageWidth, 190);
        Fill(Green, 0, PageHeight - 196, PageWidth, 6);
        if (logo is PngImage picture)
        {
            _logo = picture;
            double height = LogoWidth * picture.Height / picture.Width;
            double card = height + 20;
            double bottom = PageHeight - 30 - card;
            RoundedFill(Paper, Right - LogoWidth - 20, bottom, LogoWidth + 20, card, 8);
            Draw(LogoWidth, height, Right - LogoWidth - 10, bottom + 10);
        }
        _y = PageHeight - 76;
        Write(stamp, 9.5, Green, bold: true, Width);
        _y -= 10;
        Write(heading, 23, Paper, bold: true, Width);
        _y -= 8;
        Write(subtitle, 11.5, Paper, bold: false, Width);
        _y = PageHeight - 196 - 32;
    }

    /// <summary>A heading takes the space above it and whatever is left below: never the last line on a page.</summary>
    public void Heading(string text)
    {
        Break(74);
        _y -= 16;
        Fill(Green, Left, _y - 1, 26, 3);
        _y -= 12;
        Write(text, 14.5, Blue, bold: true, Width);
        _y -= 8;
    }

    public void Body(string text)
    {
        Write(text, 10.5, Ink, bold: false, Width);
        _y -= 5;
    }

    public void Lead(string text)
    {
        Write(text, 11.5, Quiet, bold: false, Width);
        _y -= 8;
    }

    public void Bullet(string text)
    {
        Bullet(text, 0);
    }

    /// <summary>An indented bullet hangs under the step above it, which is how a book's sheets sit under the book.</summary>
    public void Bullet(string text, double indent)
    {
        Break(30);
        double top = _y;
        double size = indent > 0 ? 10 : 10.5;
        // On the middle of the first line, not on its top: the dot is a mark beside the text, not a character in it.
        Circle(Green, Left + indent + 4, _y - (size * Leading) + 3, 2.4);
        _y = top;
        Write(text, size, Ink, bold: false, Width - indent - 18, Left + indent + 18);
        _y -= 3;
    }

    /// <summary>
    /// A numbered step, with its number in the tinted tile the site uses for an icon. The sequence is the point:
    /// a reader should be able to follow the numbers down the page without reading a word in between.
    /// </summary>
    public void Step(int number, string heading, string text)
    {
        IReadOnlyList<string> lines = Wrap(text, 10.5, Width - 42);
        Break((lines.Count * 10.5 * Leading) + 34);
        _y -= 10;
        double top = _y;
        RoundedFill(Tint, Left, _y - 24, 26, 26, 7);
        Place(number.ToString(CultureInfo.InvariantCulture), 12.5, Blue, bold: true,
            Left + 13 - (font.Measure(number.ToString(CultureInfo.InvariantCulture), 12.5) / 2), _y - 16);
        _y = top;
        Write(heading, 12, Blue, bold: true, Width - 42, Left + 42);
        _y -= 2;
        foreach (string line in lines)
        {
            _y -= 10.5 * Leading;
            Place(line, 10.5, Ink, bold: false, Left + 42);
        }
        _y -= 6;
    }

    /// <summary>A label and its answer on one line: the shape a reader scans when they want one fact.</summary>
    public void Fact(string label, string text)
    {
        Break(30);
        double top = _y;
        Write(label, 10.5, Blue, bold: true, 144, Left);
        double afterLabel = _y;
        _y = top;
        Write(text, 10.5, Ink, bold: false, Width - 150, Left + 150);
        _y = Math.Min(_y, afterLabel) - 2;
    }

    /// <summary>What the reader must not get wrong, in the card the site uses for anything it wants read.</summary>
    public void Note(string text)
    {
        IReadOnlyList<string> lines = Wrap(text, 10, Width - 40);
        double height = (lines.Count * 10 * Leading) + 22;
        Break(height + 14);
        _y -= 10;
        RoundedFill(Paper, Left, _y - height, Width, height, 8);
        RoundedEdge(Edge, Left, _y - height, Width, height, 8);
        Fill(Green, Left + 1, _y - height + 8, 3, height - 16);
        _y -= 11;
        foreach (string line in lines)
        {
            _y -= 10 * Leading;
            Place(line, 10, Ink, bold: false, Left + 20);
        }
        _y -= 15;
    }

    public void Spacer(double points)
    {
        _y -= points;
    }

    /// <summary>Start a page now if this much does not fit, so a block that must be read as one is not split.</summary>
    public void Keep(double points)
    {
        Break(points);
    }

    public void NewPage()
    {
        Footer();
        _pages.Add(_page);
        _page = new StringBuilder();
        _y = Top;
    }

    public byte[] ToBytes()
    {
        Footer();
        _pages.Add(_page);
        _page = new StringBuilder();

        List<byte[]> objects = [];
        const int pagesObject = 2;
        int fontObject = 3 + (_pages.Count * 2);
        objects.Add(Ascii($"<< /Type /Catalog /Pages {pagesObject} 0 R >>"));
        string kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(page => $"{3 + (page * 2)} 0 R"));
        objects.Add(Ascii($"<< /Type /Pages /Count {_pages.Count} /Kids [{kids}] >>"));
        for (int page = 0; page < _pages.Count; page++)
        {
            string images = _logo is null ? "" : $"/XObject << /Im1 {fontObject + 6} 0 R >> ";
            objects.Add(Ascii($"<< /Type /Page /Parent {pagesObject} 0 R /MediaBox [0 0 {PageWidth:0.##} {PageHeight:0.##}] "
                + $"/Resources << /Font << /F1 {fontObject} 0 R >> {images}>> /Contents {4 + (page * 2)} 0 R >>"));
            objects.Add(Stream("", Encoding.ASCII.GetBytes(_pages[page].ToString())));
        }

        int descendant = fontObject + 1;
        objects.Add(Ascii($"<< /Type /Font /Subtype /Type0 /BaseFont /{font.Name} /Encoding /Identity-H "
            + $"/DescendantFonts [{descendant} 0 R] /ToUnicode {descendant + 3} 0 R >>"));
        objects.Add(Ascii($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{font.Name} "
            + "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + $"/FontDescriptor {descendant + 1} 0 R /DW 1000 /W [{Widths()}] /CIDToGIDMap /Identity >>"));
        objects.Add(Ascii($"<< /Type /FontDescriptor /FontName /{font.Name} /Flags 32 "
            + $"/FontBBox [{Scaled(font.BoundingBox[0])} {Scaled(font.BoundingBox[1])} {Scaled(font.BoundingBox[2])} {Scaled(font.BoundingBox[3])}] "
            + $"/ItalicAngle 0 /Ascent {Scaled(font.Ascent)} /Descent {Scaled(font.Descent)} /CapHeight {Scaled(font.Ascent)} "
            + $"/StemV 80 /FontFile2 {descendant + 2} 0 R >>"));
        objects.Add(Stream($"/Length1 {font.File.Length} ", font.File));
        objects.Add(Stream("", Encoding.ASCII.GetBytes(ToUnicode())));
        objects.Add(Ascii($"<< /Title {Unicode(title)} /Producer (CrmWorkflowExtractor) >>"));
        if (_logo is PngImage picture)
        {
            // The colour and the transparency are two images to a PDF: the second one masks the first.
            string mask = picture.Alpha is null ? "" : $"/SMask {fontObject + 7} 0 R ";
            objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {picture.Width} /Height {picture.Height} "
                + $"/ColorSpace /DeviceRGB /BitsPerComponent 8 {mask}", picture.Rgb));
            if (picture.Alpha is byte[] opacity)
            {
                objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {picture.Width} /Height {picture.Height} "
                    + "/ColorSpace /DeviceGray /BitsPerComponent 8 ", opacity));
            }
        }

        return Assemble(objects);
    }

    private static byte[] Assemble(IReadOnlyList<byte[]> objects)
    {
        MemoryStream file = new();
        file.Write(Encoding.ASCII.GetBytes("%PDF-1.7\n"));
        file.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        List<long> offsets = [];
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(file.Position);
            file.Write(Ascii($"{index + 1} 0 obj\n"));
            file.Write(objects[index]);
            file.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }

        long startxref = file.Position;
        StringBuilder table = new();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }
        table.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info {objects.Count} 0 R >>\nstartxref\n{startxref}\n%%EOF\n");
        file.Write(Encoding.ASCII.GetBytes(table.ToString()));
        return file.ToArray();
    }

    /// <summary>Every stream is deflated: a PDF that carries a whole font file is otherwise megabytes of nothing.</summary>
    private static byte[] Stream(string extra, byte[] data)
    {
        MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data);
        }
        byte[] body = compressed.ToArray();
        MemoryStream stream = new();
        stream.Write(Ascii($"<< {extra}/Length {body.Length} /Filter /FlateDecode >>\nstream\n"));
        stream.Write(body);
        stream.Write(Encoding.ASCII.GetBytes("\nendstream"));
        return stream.ToArray();
    }

    private static byte[] Ascii(string text)
    {
        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>A PDF text string the reader shows in its own window title, so it is written as UTF-16.</summary>
    private static string Unicode(string value)
    {
        StringBuilder hex = new("<FEFF");
        foreach (char letter in value)
        {
            hex.Append(CultureInfo.InvariantCulture, $"{(int)letter:X4}");
        }
        return hex.Append('>').ToString();
    }

    private int Scaled(int units)
    {
        return units * 1000 / font.UnitsPerEm;
    }

    private string Widths()
    {
        StringBuilder widths = new();
        foreach (int glyph in _used)
        {
            widths.Append(CultureInfo.InvariantCulture, $"{glyph} [{font.Width(glyph)}] ");
        }
        return widths.ToString().TrimEnd();
    }

    private string ToUnicode()
    {
        StringBuilder map = new();
        map.Append("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n")
            .Append("/CMapName /Identity-H def /CMapType 2 def\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> def\n")
            .Append("1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
        // A CMap takes at most 100 mappings in one block, and a guide uses more characters than that.
        foreach (int[] block in _used.Chunk(100))
        {
            map.Append(CultureInfo.InvariantCulture, $"{block.Length} beginbfchar\n");
            foreach (int glyph in block)
            {
                map.Append(CultureInfo.InvariantCulture, $"<{glyph:X4}> <{(int)_letters[glyph]:X4}>\n");
            }
            map.Append("endbfchar\n");
        }
        return map.Append("endcmap CMapName currentdict /CMap defineresource pop end end").ToString();
    }

    /// <summary>
    /// A picture is drawn into the unit square, so the matrix that places it is also the one that sizes it.
    /// </summary>
    private void Draw(double width, double height, double x, double y)
    {
        _page.Append(CultureInfo.InvariantCulture, $"q {width:0.##} 0 0 {height:0.##} {x:0.##} {y:0.##} cm /Im1 Do Q\n");
    }

    private void Fill(string colour, double x, double y, double width, double height)
    {
        _page.Append(CultureInfo.InvariantCulture, $"{colour} rg {x:0.##} {y:0.##} {width:0.##} {height:0.##} re f\n");
    }

    private void Circle(string colour, double x, double y, double radius)
    {
        double pull = radius * Kappa;
        _page.Append(CultureInfo.InvariantCulture,
            $"{colour} rg {x - radius:0.##} {y:0.##} m "
            + $"{x - radius:0.##} {y + pull:0.##} {x - pull:0.##} {y + radius:0.##} {x:0.##} {y + radius:0.##} c "
            + $"{x + pull:0.##} {y + radius:0.##} {x + radius:0.##} {y + pull:0.##} {x + radius:0.##} {y:0.##} c "
            + $"{x + radius:0.##} {y - pull:0.##} {x + pull:0.##} {y - radius:0.##} {x:0.##} {y - radius:0.##} c "
            + $"{x - pull:0.##} {y - radius:0.##} {x - radius:0.##} {y - pull:0.##} {x - radius:0.##} {y:0.##} c f\n");
    }

    private void RoundedFill(string colour, double x, double y, double width, double height, double radius)
    {
        _page.Append(CultureInfo.InvariantCulture, $"{colour} rg {RoundedPath(x, y, width, height, radius)} f\n");
    }

    private void RoundedEdge(string colour, double x, double y, double width, double height, double radius)
    {
        _page.Append(CultureInfo.InvariantCulture, $"{colour} RG 0.8 w {RoundedPath(x, y, width, height, radius)} S\n");
    }

    private static string RoundedPath(double x, double y, double width, double height, double radius)
    {
        double pull = radius * Kappa;
        double right = x + width;
        double top = y + height;
        return string.Create(CultureInfo.InvariantCulture,
            $"{x + radius:0.##} {y:0.##} m {right - radius:0.##} {y:0.##} l "
            + $"{right - radius + pull:0.##} {y:0.##} {right:0.##} {y + radius - pull:0.##} {right:0.##} {y + radius:0.##} c "
            + $"{right:0.##} {top - radius:0.##} l "
            + $"{right:0.##} {top - radius + pull:0.##} {right - radius + pull:0.##} {top:0.##} {right - radius:0.##} {top:0.##} c "
            + $"{x + radius:0.##} {top:0.##} l "
            + $"{x + radius - pull:0.##} {top:0.##} {x:0.##} {top - radius + pull:0.##} {x:0.##} {top - radius:0.##} c "
            + $"{x:0.##} {y + radius:0.##} l "
            + $"{x:0.##} {y + radius - pull:0.##} {x + radius - pull:0.##} {y:0.##} {x + radius:0.##} {y:0.##} c h");
    }

    private void Footer()
    {
        Fill(Edge, Left, Bottom - 16, Width, 0.6);
        Place(title, 8.5, Quiet, bold: false, Left, Bottom - 30);
        string number = (_pages.Count + 1).ToString(CultureInfo.InvariantCulture);
        Place(number, 8.5, Quiet, bold: false, Right - font.Measure(number, 8.5), Bottom - 30);
    }

    private void Break(double needed)
    {
        if (_y - needed < Bottom)
        {
            NewPage();
        }
    }

    private void Write(string text, double size, string colour, bool bold, double width)
    {
        Write(text, size, colour, bold, width, Left);
    }

    private void Write(string text, double size, string colour, bool bold, double width, double x)
    {
        foreach (string line in Wrap(text, size, width))
        {
            Break(size * Leading);
            _y -= size * Leading;
            Place(line, size, colour, bold, x);
        }
    }

    private void Place(string line, double size, string colour, bool bold, double x)
    {
        Place(line, size, colour, bold, x, _y);
    }

    private void Place(string line, double size, string colour, bool bold, double x, double y)
    {
        // There is one font file in the document, so bold is drawn rather than loaded: fill and stroke the same
        // glyphs. At this size the difference from a real bold face is not one a reader notices.
        string weight = bold ? "2 Tr 0.3 w " : "0 Tr ";
        _page.Append(CultureInfo.InvariantCulture,
            $"{colour} rg {colour} RG {weight}BT /F1 {size:0.##} Tf 1 0 0 1 {x:0.##} {y:0.##} Tm {Hex(line)} Tj ET 0 Tr\n");
    }

    private IReadOnlyList<string> Wrap(string text, double size, double width)
    {
        List<string> lines = [];
        StringBuilder line = new();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.Measure(candidate, size) > width)
            {
                lines.Add(line.ToString());
                line.Clear().Append(word);
                continue;
            }
            line.Clear().Append(candidate);
        }
        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }
        return lines;
    }

    private string Hex(string text)
    {
        StringBuilder hex = new("<");
        foreach (char letter in text)
        {
            int glyph = font.Glyph(letter);
            _used.Add(glyph);
            _letters[glyph] = letter;
            hex.Append(CultureInfo.InvariantCulture, $"{glyph:X4}");
        }
        return hex.Append('>').ToString();
    }
}
