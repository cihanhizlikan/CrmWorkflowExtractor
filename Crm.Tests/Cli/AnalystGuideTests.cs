using System.Text;
using Crm.Cli;
using Crm.Cli.Reports;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The guide an analyst reads before anything else. A PDF is checked the way the .xlsx is: by opening the bytes
/// back up — the object table has to point where it says, the Turkish letters have to have real glyphs behind
/// them, and the same run twice has to produce the same file.
/// </summary>
public sealed class AnalystGuideTests
{
    [Fact]
    public async Task A_Run_Writes_The_Guide_As_A_Readable_Pdf()
    {
        using TemporaryOutput output = new();

        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8 }.Build();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.True(code == ExitCode.Success, console);
        string file = Path.Combine(runRoot, "raporlar", "nasil-kullanilir.pdf");
        Assert.True(File.Exists(file), "Kılavuz üretilmedi.");
        byte[] pdf = File.ReadAllBytes(file);
        Assert.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", Encoding.ASCII.GetString(pdf, pdf.Length - 6, 6), StringComparison.Ordinal);

        // Every offset in the cross-reference table has to land on the object it claims: a reader that follows one
        // into the middle of a stream shows a blank page rather than an error.
        string text = Encoding.Latin1.GetString(pdf);
        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        int table = text.LastIndexOf("xref\n0 ", StringComparison.Ordinal);
        Assert.True(table > 0 && startxref > table);
        string[] lines = text[table..startxref].Split('\n');
        int objects = int.Parse(lines[1].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture) - 1;
        for (int index = 1; index <= objects; index++)
        {
            int offset = int.Parse(lines[index + 2][..10], System.Globalization.CultureInfo.InvariantCulture);
            Assert.StartsWith($"{index} 0 obj", text[offset..], StringComparison.Ordinal);
        }

        Assert.Contains("/Subtype /Type0", text, StringComparison.Ordinal);
        Assert.Contains("/Encoding /Identity-H", text, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", text, StringComparison.Ordinal);
        // The cover carries the logo: the picture is in the file and the page offers it as a resource. That it is
        // also drawn is inside the deflated content stream, so it is checked by looking at the rendered page.
        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.Contains("/XObject << /Im1", text, StringComparison.Ordinal);
    }

    /// <summary>Two runs over the same evidence must give the same bytes, or nobody can tell a re-run from a change.</summary>
    [Fact]
    public void The_Same_Guide_Twice_Is_The_Same_File()
    {
        TrueTypeFont font = Assert.IsType<TrueTypeFont>(TrueTypeFont.FindInstalled());

        Assert.Equal(Sample(font), Sample(font));
    }

    /// <summary>
    /// The reason the font is embedded at all: the letters that separate Turkish from the PDF base encodings.
    /// If any of them fell back to a space the guide would be misspelled everywhere and still look fine to a test.
    /// </summary>
    [Fact]
    public void The_Turkish_Letters_Have_Glyphs_Of_Their_Own()
    {
        TrueTypeFont font = Assert.IsType<TrueTypeFont>(TrueTypeFont.FindInstalled());

        Assert.True(font.Covers("ğışİĞŞçöüÇÖÜâîû"));
        int space = font.Glyph(' ');
        Assert.All("ğışİĞŞ", letter => Assert.NotEqual(space, font.Glyph(letter)));
        Assert.All("ğışİĞŞ", letter => Assert.True(font.Width(font.Glyph(letter)) > 0));
    }

    private static byte[] Sample(TrueTypeFont font)
    {
        using Stream? resource = typeof(AnalystGuide).Assembly.GetManifestResourceStream("Crm.Cli.Resources.kurumsal-logo.png");
        MemoryStream copy = new();
        resource!.CopyTo(copy);
        PdfDocument pdf = new(font, "Kılavuz");
        pdf.Banner("Başlık", "Alt başlık", "DAMGA", PngImage.TryRead(copy.ToArray()));
        pdf.Heading("Bölüm");
        pdf.Body("Gövde metni: ğışİĞŞ.");
        pdf.Step(1, "Adım", "Yapılacak iş.");
        pdf.Bullet("Madde");
        pdf.Fact("Etiket", "Değer");
        pdf.Note("Uyarı");
        return pdf.ToBytes();
    }
}
