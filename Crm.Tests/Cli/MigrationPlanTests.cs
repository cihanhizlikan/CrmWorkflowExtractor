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

        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains(plan.Rows("BPMN dizini"), row => row.Count > 1
            && row[0] == "is-akisi/new-policy/police-iptal-sureci-0.bpmn" && row[1] == "Poliçe İptal Süreci 0");
    }

    [Fact]
    public async Task The_Worksheet_Has_A_Row_Per_Workflow_Sorted_Live_Processes_First()
    {
        using TemporaryOutput output = new();

        (_, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        Workbook workbook = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Equal(["Nasıl okunur", "Taşıma planı", "Kullanım", "Çağrı ağacı", "Süreç ağaçları", "BPMN dizini",
        "Okunamayan yapılar", "Yapı sıklığı", "Sapma"], workbook.Names);
        // The guide travels inside the workbook, so a reader who has the file has the column meanings too.
        Assert.Contains(workbook.Rows("Nasıl okunur"), row => row.Count > 1 && row[0] == "Bu kitap ne işe yarar");
        IReadOnlyList<IReadOnlyList<string>> rows = workbook.Rows("Taşıma planı");
        Assert.Equal(1 + 46, rows.Count);
        Assert.Equal(["öncelik", "is_akisi", "bpmn_dosyasi"], rows[0].Take(3));
        // Priority 1 is a live process; the 44 drafts (43 with XAML) sort last. The band is a number, so Excel sorts it.
        Assert.Equal("1", rows[1][0]);
        Assert.Equal("4", rows[^1][0]);
        Assert.True(workbook.IsNumeric("Taşıma planı", 1, 0));
        Assert.True(workbook.HasFrozenHeaderAndFilter("Taşıma planı"));
        Assert.Contains(rows, row => row.Any(cell => cell.Contains("Kullanılıyor: son kayıtlı çalışma 2026-09-20", StringComparison.Ordinal)));
        Assert.Contains(rows, row => row.Any(cell => cell.Contains("is-akisi/new-policy/police-iptal-sureci-0.bpmn", StringComparison.Ordinal)));
        Assert.False(File.Exists(Path.Combine(runRoot, "raporlar", "tasima-plani.md")), "Kitaba taşınan sayfa ayrıca dosya olarak üretilmemeli.");
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
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains(plan.Rows("Çağrı ağacı"), row => row.Contains("giriş noktası"));
        // A process tree is one migration unit: the root, then what it calls, by depth.
        Assert.Contains(plan.Rows("Süreç ağaçları"), row => row.Count > 1 && row[1] == "0");
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
        Workbook families = Workbook.Open(Path.Combine(runRoot, "raporlar", "aileler.xlsx"));
        Assert.Contains(families.Rows("Ürünle gelenler"), row => row.Contains("Poliçe İptal Süreci 6"));
        Assert.DoesNotContain(families.Rows("Aileler"), row => row.Contains("Poliçe İptal Süreci 6"));

        // It keeps its diagram and its row, marked, so nothing disappears silently.
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains(plan.Rows("Taşıma planı"), row => row.Count > 1 && row[0] == "5" && row[1] == "Poliçe İptal Süreci 6");
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
