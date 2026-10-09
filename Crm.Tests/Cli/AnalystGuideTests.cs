using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Crm.Cli;
using Crm.Cli.Reports;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The guide an analyst reads before anything else, now a Word document in the department's own standard layout.
/// It is checked the way the .xlsx is: by opening the bytes back up. A .docx that Word refuses is worse than no
/// guide at all, because nobody finds out until the package is already with the partner.
/// </summary>
public sealed class AnalystGuideTests
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Dictionary<string, byte[]> Parts(byte[] docx)
    {
        Dictionary<string, byte[]> parts = new(StringComparer.Ordinal);
        using MemoryStream file = new(docx);
        using ZipArchive zip = new(file, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream stream = entry.Open();
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            parts[entry.FullName] = copy.ToArray();
        }
        return parts;
    }

    private static XDocument Xml(Dictionary<string, byte[]> parts, string path)
    {
        return XDocument.Parse(Encoding.UTF8.GetString(parts[path]));
    }

    private static string Words(XDocument document)
    {
        return string.Join(" ", document.Descendants(Main + "t").Select(text => text.Value));
    }

    [Fact]
    public async Task A_Run_Writes_The_Guide_As_A_Word_Document()
    {
        using TemporaryOutput output = new();
        FakeOrganization organization = new() { WorkflowCount = 8 };

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);
        string file = Path.Combine(runRoot, "raporlar", "nasil-kullanilir.docx");
        Assert.True(File.Exists(file), "Kılavuz üretilmedi.");

        Dictionary<string, byte[]> parts = Parts(File.ReadAllBytes(file));
        // The four parts Word needs to open the file at all, and the picture the cover carries.
        Assert.Contains("[Content_Types].xml", parts.Keys);
        Assert.Contains("_rels/.rels", parts.Keys);
        Assert.Contains("word/document.xml", parts.Keys);
        Assert.Contains("word/styles.xml", parts.Keys);
        Assert.Contains("word/media/logo.png", parts.Keys);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], parts["word/media/logo.png"][..4]);

        // Every relationship the document refers to has to exist, or Word repairs the file on open.
        XDocument body = Xml(parts, "word/document.xml");
        XDocument links = Xml(parts, "word/_rels/document.xml.rels");
        List<string> declared = [.. links.Root!.Elements().Select(link => link.Attribute("Id")!.Value)];
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (XAttribute used in body.Descendants().Attributes(relationships + "embed"))
        {
            Assert.Contains(used.Value, declared);
        }

        // Every style a paragraph asks for has to be defined, or the text falls back to Word's own look.
        List<string> defined = [.. Xml(parts, "word/styles.xml").Descendants(Main + "style")
            .Select(style => style.Attribute(Main + "styleId")!.Value)];
        foreach (XElement used in body.Descendants(Main + "pStyle"))
        {
            Assert.Contains(used.Attribute(Main + "val")!.Value, defined);
        }
    }

    /// <summary>
    /// The first delivery is the original diagrams only. A guide that named the combined models or the families
    /// workbook would send the reader looking for what they were not given.
    /// </summary>
    [Fact]
    public async Task The_Guide_Names_Nothing_The_Reader_Was_Not_Given()
    {
        using TemporaryOutput output = new();
        FakeOrganization organization = new() { WorkflowCount = 8 };

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);
        string text = Words(Xml(Parts(File.ReadAllBytes(Path.Combine(runRoot, "raporlar", "nasil-kullanilir.docx"))), "word/document.xml"));

        foreach (string absent in new[] { "aileler", "birlesik", "Birleşik", "Aile", "kapsam-disi", "hassas-degerler",
            "ara-model", "elle-inceleme", "calistirma-yetkisi", "Çalıştırma yetkisi" })
        {
            Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
        }
        // And it still says what it is for.
        Assert.Contains("tasima-plani.xlsx", text, StringComparison.Ordinal);
        Assert.Contains("dis-sistemler.xlsx", text, StringComparison.Ordinal);
    }

    /// <summary>Two runs over the same evidence must give the same bytes, or nobody can tell a re-run from a change.</summary>
    [Fact]
    public void The_Same_Guide_Twice_Is_The_Same_File()
    {
        Assert.Equal(Sample(), Sample());
    }

    /// <summary>
    /// Turkish is the whole document's language, and XML has five characters it cannot carry raw. A control
    /// character scanned out of a CRM definition is not XML at any encoding and would make Word refuse the file.
    /// </summary>
    [Fact]
    public void Turkish_Survives_And_What_Xml_Cannot_Carry_Does_Not()
    {
        byte[] docx = WordDocument.Build(
            [new Paragraph("Normal", "Poliçe & İş Akışı <ğışİĞŞ> \"alıntı\"\u0001")], null);

        string text = Words(Xml(Parts(docx), "word/document.xml"));
        Assert.Equal("Poliçe & İş Akışı <ğışİĞŞ> \"alıntı\"", text);
    }

    /// <summary>
    /// WordprocessingML fixes the ORDER of the children of w:pPr and w:rPr, and a file that puts outlineLvl
    /// before spacing, or sz before color, is one Word calls unreadable and "repairs" — which is to say throws
    /// away. It opens on the reader's desk, not here, so it is checked here.
    /// </summary>
    [Fact]
    public void Style_Properties_Are_In_The_Order_The_Schema_Demands()
    {
        string[] paragraphOrder = ["keepNext", "spacing", "ind", "jc", "outlineLvl"];
        string[] runOrder = ["rFonts", "b", "color", "sz"];

        XDocument styles = Xml(Parts(Sample()), "word/styles.xml");

        foreach (XElement style in styles.Descendants(Main + "style"))
        {
            Assert.Equal(Ordered(style, Main + "pPr", paragraphOrder), Named(style, Main + "pPr"));
            Assert.Equal(Ordered(style, Main + "rPr", runOrder), Named(style, Main + "rPr"));
        }
    }

    private static List<string> Named(XElement style, XName part)
    {
        return [.. style.Element(part)?.Elements().Select(child => child.Name.LocalName) ?? []];
    }

    private static List<string> Ordered(XElement style, XName part, string[] order)
    {
        List<string> present = Named(style, part);
        return [.. order.Where(present.Contains)];
    }

    private static byte[] Sample()
    {
        return WordDocument.Build(
        [
            new Paragraph("CoverMainTitle", "Başlık"),
            new Paragraph("CoverDocInfo", "Kurumsal Mimari", "Hazırlayan: "),
            new PageBreak(),
            new Paragraph("Heading1", "1  Bölüm"),
            new Paragraph("Normal", "Gövde metni: ğışİĞŞ."),
            new Table([new DocumentRow(["Terim", "Anlamı"], Header: true), new DocumentRow(["Aile", "—"])], [2600, 7000])
        ], null);
    }
}
