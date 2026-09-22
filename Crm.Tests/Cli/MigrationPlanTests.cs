using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Cli;
using Crm.Tests.Bpmn;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>What an analyst is handed: files grouped into folders, a worksheet, a call graph, and a note on each diagram.</summary>
public sealed class MigrationPlanTests
{
    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", name);
    }

    [Fact]
    public async Task Diagrams_Are_Filed_By_Category_And_Entity_With_An_Index()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: Fixture("mock-crm-export.json"));

        Assert.True(code == ExitCode.Success, console);
        string bpmn = Path.Combine(runRoot, "bpmn");
        Assert.True(File.Exists(Path.Combine(bpmn, "is-akisi", "new-policy", "police-iptal-sureci-0.bpmn")));
        Assert.True(File.Exists(Path.Combine(bpmn, "is-akisi", "new-claim", "hasar-onay-10.bpmn")));
        Assert.Empty(Directory.GetFiles(bpmn, "*.bpmn", SearchOption.TopDirectoryOnly));

        string index = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(bpmn, "dizin.csv")));
        Assert.Contains("is-akisi/new-policy/police-iptal-sureci-0.bpmn;Poliçe İptal Süreci 0;", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Worksheet_Has_A_Row_Per_Workflow_Sorted_Live_Processes_First()
    {
        using TemporaryOutput output = new();

        (_, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        string[] lines = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "raporlar", "tasima-plani.csv")))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 46, lines.Length);
        Assert.StartsWith("öncelik;is_akisi;bpmn_dosyasi;", lines[0].TrimStart('﻿'), StringComparison.Ordinal);
        // Priority 1 is a live process; the 44 drafts (43 with XAML) sort last.
        Assert.StartsWith("1;", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("4;", lines[^1], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("Kullanılıyor: son kayıtlı çalışma 2026-09-20", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("is-akisi/new-policy/police-iptal-sureci-0.bpmn", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(runRoot, "raporlar", "tasima-plani.md")), console);
    }

    [Fact]
    public async Task The_Call_Graph_Separates_Entry_Points_From_Building_Blocks()
    {
        // Three definitions call a child workflow that is itself one of the three (see the child-and-custom fixture).
        string xaml = Ir.XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, XamlFor = (_, _) => xaml }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.True(code == ExitCode.Success, console);
        string graph = File.ReadAllText(Path.Combine(runRoot, "raporlar", "cagri-agaci.md"));
        Assert.Contains("alt iş akışı çağrısı", graph, StringComparison.Ordinal);
        Assert.Contains("bu çalıştırmada bulunmayan", graph, StringComparison.Ordinal);
        string csv = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "raporlar", "cagri-agaci.csv")));
        Assert.Contains("giriş noktası", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// Workflows that came with the product are not this company's to rebuild. The split is CRM's own
    /// <c>ismanaged</c> flag — never a guess from the name, which would be a judgement the tool cannot make.
    /// </summary>
    [Fact]
    public async Task Workflows_Supplied_With_The_Product_Are_Held_Apart_From_The_Work()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 8, Managed = new HashSet<int> { 6 } }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.True(code == ExitCode.Success, console);
        string supplied = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "aileler", "urunle-gelenler.csv")));
        Assert.Contains("Poliçe İptal Süreci 6", supplied, StringComparison.Ordinal);
        Assert.DoesNotContain("Poliçe İptal Süreci 6", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "aileler", "aileler.csv"))), StringComparison.Ordinal);

        // It keeps its diagram and its row, marked, so nothing disappears silently.
        Assert.Contains("5;Poliçe İptal Süreci 6;", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "raporlar", "tasima-plani.csv"))), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runRoot, "bpmn", "is-akisi", "new-policy", "police-iptal-sureci-6.bpmn")));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        Assert.All(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— uygun", link.GetString(), StringComparison.Ordinal));
        Assert.Contains(manifest.RootElement.GetProperty("countChain").EnumerateArray(),
            link => link.GetString()!.Contains("ürünle gelen", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_Diagram_Carries_A_Header_Note_Above_The_Flow()
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor("condition-update-stop.xaml")), "test");

        XElement note = Assert.Single(xml.Descendants(BpmnSerializer.Model + "textAnnotation"));
        string text = note.Element(BpmnSerializer.Model + "text")!.Value;
        Assert.Contains("Poliçe İptal", text, StringComparison.Ordinal);
        Assert.Contains("İş Akışı · Arka plan · Etkin · varlık: new_policy", text, StringComparison.Ordinal);
        Assert.Contains("Şununla başlar:", text, StringComparison.Ordinal);
        Assert.Contains("Çalıştırılabilir değildir", text, StringComparison.Ordinal);
        Assert.Single(xml.Descendants(BpmnSerializer.Model + "association"));

        // The note is drawn above everything else, so no shape sits on it.
        XElement noteBounds = xml.Descendants(BpmnSerializer.Di + "BPMNShape")
            .Single(shape => shape.Attribute("bpmnElement")!.Value.StartsWith("note_", StringComparison.Ordinal))
            .Element(BpmnSerializer.Dc + "Bounds")!;
        double noteBottom = double.Parse(noteBounds.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            + double.Parse(noteBounds.Attribute("height")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        foreach (XElement shape in xml.Descendants(BpmnSerializer.Di + "BPMNShape")
            .Where(shape => !shape.Attribute("bpmnElement")!.Value.StartsWith("note_", StringComparison.Ordinal)))
        {
            double top = double.Parse(shape.Element(BpmnSerializer.Dc + "Bounds")!.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(top > noteBottom, "A shape overlaps the header note.");
        }
    }

    [Fact]
    public void A_Draft_Says_So_On_Its_Diagram()
    {
        Crm.Ir.Model.WorkflowIr ir = BpmnEmissionTests.IrFor("condition-update-stop.xaml");
        Crm.Ir.Model.WorkflowIr draft = ir with { Identity = ir.Identity with { State = "Taslak" } };

        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(draft), "test");

        Assert.Contains("CRM'de TASLAK: yeni çalıştırma başlatamaz.",
            xml.Descendants(BpmnSerializer.Model + "text").Single().Value, StringComparison.Ordinal);
    }
}
