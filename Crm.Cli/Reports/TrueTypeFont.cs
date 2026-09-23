using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// As much of a TrueType file as embedding it in a PDF needs: the glyph for a character, its advance width, and the
/// metrics a PDF reader wants before it will draw with the file.
///
/// <para>
/// It exists because the output is Turkish and the PDF base fonts are not. A reader's built-in Helvetica is encoded
/// WinAnsi, which has ö and ü but no ğ, ı or ş — a guide written in it would be misspelled on every other line. The
/// only way to set those letters is to carry a font that has them, so the file travels inside the PDF.
/// </para>
/// </summary>
public sealed class TrueTypeFont
{
    /// <summary>Bit 1 of OS/2 fsType: the foundry forbids embedding this file. Then it is not ours to embed.</summary>
    private const int RestrictedLicense = 0x0002;

    private readonly byte[] _file;
    private readonly Dictionary<string, int> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<char, int> _glyphs = [];
    private readonly ushort[] _advances;

    private TrueTypeFont(byte[] file)
    {
        _file = file;
        int numTables = UInt16At(4);
        for (int table = 0; table < numTables; table++)
        {
            int record = 12 + (table * 16);
            _tables[Encoding.ASCII.GetString(file, record, 4)] = Int32At(record + 8);
        }

        int head = Table("head");
        UnitsPerEm = UInt16At(head + 18);
        BoundingBox = [Int16At(head + 36), Int16At(head + 38), Int16At(head + 40), Int16At(head + 42)];
        int hhea = Table("hhea");
        Ascent = Int16At(hhea + 4);
        Descent = Int16At(hhea + 6);
        int metrics = UInt16At(hhea + 34);
        int hmtx = Table("hmtx");
        _advances = new ushort[Math.Max(metrics, 1)];
        for (int glyph = 0; glyph < metrics; glyph++)
        {
            _advances[glyph] = (ushort)UInt16At(hmtx + (glyph * 4));
        }
        ReadCharacterMap();
        Name = PostScriptName();
    }

    /// <summary>The PostScript name, which is what the PDF calls the font.</summary>
    public string Name { get; }

    public int UnitsPerEm { get; }

    public int Ascent { get; }

    public int Descent { get; }

    public IReadOnlyList<int> BoundingBox { get; }

    public byte[] File
    {
        get { return _file; }
    }

    /// <summary>
    /// The first font on this machine that has the Turkish letters and whose own licence allows embedding, or null
    /// when there is none — in which case the caller writes no PDF and says so, rather than writing a misspelled one.
    /// </summary>
    public static TrueTypeFont? FindInstalled()
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] candidates = ["arial.ttf", "segoeui.ttf", "tahoma.ttf", "verdana.ttf", "DejaVuSans.ttf"];
        foreach (string candidate in candidates)
        {
            string path = Path.Combine(folder, candidate);
            if (!System.IO.File.Exists(path))
            {
                continue;
            }
            TrueTypeFont? font = TryRead(System.IO.File.ReadAllBytes(path));
            if (font is not null && font.Covers("ğışİĞŞçöüÇÖÜ"))
            {
                return font;
            }
        }
        return null;
    }

    public static TrueTypeFont? TryRead(byte[] file)
    {
        try
        {
            TrueTypeFont font = new(file);
            return font.Allowed() ? font : null;
        }
        catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }

    public bool Covers(string text)
    {
        return text.All(letter => _glyphs.ContainsKey(letter));
    }

    /// <summary>The glyph that draws this character, or the space glyph when the font does not have it.</summary>
    public int Glyph(char letter)
    {
        return _glyphs.TryGetValue(letter, out int glyph) ? glyph : _glyphs.GetValueOrDefault(' ');
    }

    /// <summary>The advance in PDF text space, where the em is 1000 units whatever the font calls it.</summary>
    public int Width(int glyph)
    {
        ushort advance = glyph < _advances.Length ? _advances[glyph] : _advances[^1];
        return advance * 1000 / UnitsPerEm;
    }

    public double Measure(string text, double size)
    {
        double width = 0;
        foreach (char letter in text)
        {
            width += Width(Glyph(letter));
        }
        return width * size / 1000;
    }

    private bool Allowed()
    {
        if (!_tables.ContainsKey("OS/2"))
        {
            return true;
        }
        return (UInt16At(Table("OS/2") + 8) & RestrictedLicense) == 0;
    }

    /// <summary>Name record 6, in the Windows encoding: the name the PDF must use for the font.</summary>
    private string PostScriptName()
    {
        int name = Table("name");
        int count = UInt16At(name + 2);
        int strings = name + UInt16At(name + 4);
        for (int record = 0; record < count; record++)
        {
            int at = name + 6 + (record * 12);
            if (UInt16At(at) != 3 || UInt16At(at + 6) != 6)
            {
                continue;
            }
            string value = Encoding.BigEndianUnicode.GetString(_file, strings + UInt16At(at + 10), UInt16At(at + 8));
            string clean = new([.. value.Where(Nameable)]);
            if (clean.Length > 0)
            {
                return clean;
            }
        }
        return "EmbeddedFont";
    }

    /// <summary>A PDF name may not carry a delimiter or white space, and this one is written without escaping.</summary>
    private static bool Nameable(char letter)
    {
        return letter is > ' ' and < (char)127
            and not ('(' or ')' or '/' or '<' or '>' or '[' or ']' or '{' or '}' or '%' or '#');
    }

    /// <summary>The Windows Unicode subtable, format 4 — the one every Windows font has.</summary>
    private void ReadCharacterMap()
    {
        int cmap = Table("cmap");
        int count = UInt16At(cmap + 2);
        int subtable = 0;
        for (int record = 0; record < count; record++)
        {
            int at = cmap + 4 + (record * 8);
            if (UInt16At(at) == 3 && UInt16At(at + 2) is 1 or 0)
            {
                subtable = cmap + Int32At(at + 4);
            }
        }
        if (subtable == 0 || UInt16At(subtable) != 4)
        {
            throw new ArgumentException("Font has no Windows Unicode character map.");
        }

        int segments = UInt16At(subtable + 6) / 2;
        int ends = subtable + 14;
        int starts = ends + (segments * 2) + 2;
        int deltas = starts + (segments * 2);
        int ranges = deltas + (segments * 2);
        for (int segment = 0; segment < segments; segment++)
        {
            int last = UInt16At(ends + (segment * 2));
            int first = UInt16At(starts + (segment * 2));
            int delta = Int16At(deltas + (segment * 2));
            int rangeOffset = UInt16At(ranges + (segment * 2));
            for (int code = first; code <= last && code != 0xFFFF; code++)
            {
                if (rangeOffset == 0)
                {
                    _glyphs[(char)code] = (code + delta) & 0xFFFF;
                    continue;
                }
                int glyph = UInt16At(ranges + (segment * 2) + rangeOffset + ((code - first) * 2));
                if (glyph != 0)
                {
                    _glyphs[(char)code] = (glyph + delta) & 0xFFFF;
                }
            }
        }
    }

    private int Table(string tag)
    {
        return _tables.TryGetValue(tag, out int offset)
            ? offset
            : throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Font has no {tag} table."));
    }

    private int UInt16At(int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(_file.AsSpan(offset, 2));
    }

    private int Int16At(int offset)
    {
        return BinaryPrimitives.ReadInt16BigEndian(_file.AsSpan(offset, 2));
    }

    private int Int32At(int offset)
    {
        return BinaryPrimitives.ReadInt32BigEndian(_file.AsSpan(offset, 4));
    }
}
