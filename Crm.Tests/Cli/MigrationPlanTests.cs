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
    public async Task Diagrams_Are_Filed_By_Category_And_Entity()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.ImportAsync(output, Fixture("mock-crm-export.json"));

        Assert.True(code == ExitCode.Success, console);
        string bpmn = Path.Combine(runRoot, "bpmn", "is-akislari");
        Assert.True(File.Exists(Path.Combine(bpmn, "is-akisi", "new-policy", "police-iptal-sureci-0.bpmn")));
        // A draft is out of scope: its diagram is kept, but outside the delivered folder.
        Assert.False(File.Exists(Path.Combine(bpmn, "is-akisi", "new-claim", "hasar-onay-10.bpmn")));
        Assert.True(File.Exists(Path.Combine(runRoot, "kapsam-disi-bpmn", "is-akisi", "new-claim", "hasar-onay-10.bpmn")));
        Assert.Empty(Directory.GetFiles(bpmn, "*.bpmn", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.TopDirectoryOnly));

        // The plan carries the file beside the workflow, so no second index sheet has to be kept in step with it.
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        // Every path is written from the package root, so a reader opens it as it stands.
        Assert.Contains(plan.Rows("İş akışları"), row => row.Count > 1 && row[0] == "Poliçe İptal Süreci 0"
            && row.Contains("bpmn/is-akislari/is-akisi/new-policy/police-iptal-sureci-0.bpmn"));
        // Every written diagram says what calls it, which is only known after the whole run has been read.
        Assert.Contains("Rol: ", File.ReadAllText(Path.Combine(bpmn, "is-akisi", "new-policy", "police-iptal-sureci-0.bpmn")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Worksheet_Has_A_Row_Per_Workflow_Sorted_Live_Processes_First()
    {
        using TemporaryOutput output = new();

        (_, string runRoot, string console) = await RunHarness.ImportAsync(output,
            Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        Workbook workbook = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        // The sheets are in the order the guide walks them.
        Assert.Equal(["Nasıl okunur", "İş akışları", "İş akışı ağaçları"], workbook.Names);
        // The guide travels inside the workbook, so a reader who has the file has the column meanings too.
        Assert.Contains(workbook.Rows("Nasıl okunur"), row => row.Count > 1 && row[0] == "Bu kitap ne işe yarar");
        IReadOnlyList<IReadOnlyList<string>> rows = workbook.Rows("İş akışları");
        Assert.Equal(["is_akisi", "kategori", "kayit_turu"], rows[0].Take(3));
        // The plan is the work itself: nothing in it is a draft, and nobody has to filter it to find the real rows.
        Assert.DoesNotContain(rows.Skip(1), row => row.Contains("Taslak"));
        // Every column is one an analyst acts on: a count they can sort, a flag they can filter, a file they open.
        Assert.Contains("bekleme_var", rows[0]);
        Assert.DoesNotContain("durum", rows[0]);
        Assert.True(workbook.IsNumeric("İş akışları", 1, rows[0].ToList().IndexOf("adim_sayisi")));
        Assert.True(workbook.HasFrozenHeaderAndFilter("İş akışları"));
        // The verdict is a word to filter on; the date has a column of its own.
        IReadOnlyList<string> ran = Assert.Single(rows, row => row.Contains("2026-09-20"));
        Assert.Equal("çalışıyor", ran[rows[0].ToList().IndexOf("kullanim")]);
        Assert.Equal("2026-09-20", ran[rows[0].ToList().IndexOf("son_kayitli_calisma")]);
        Assert.Contains(rows, row => row.Any(cell => cell.Contains("bpmn/is-akislari/is-akisi/new-policy/police-iptal-sureci-0.bpmn", StringComparison.Ordinal)));
        Assert.False(File.Exists(Path.Combine(runRoot, "raporlar", "tasima-plani.md")), "Kitaba taşınan sayfa ayrıca dosya olarak üretilmemeli.");
    }

    [Fact]
    public async Task The_Call_Graph_Separates_Entry_Points_From_Building_Blocks()
    {
        // Three definitions call a child workflow that is itself one of the three (see the child-and-custom fixture).
        string xaml = Ir.XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeOrganization organization = new() { WorkflowCount = 8, XamlFor = (_, _) => xaml };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);
        // Who calls whom is on the workflow's own row, under its own header: the sheet that repeated the name to carry
        // it is gone.
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.DoesNotContain("Çağrı ağacı", plan.Names);
        IReadOnlyList<IReadOnlyList<string>> rows = plan.Rows("İş akışları");
        int children = rows[0].ToList().IndexOf("cagirdigi_is_akislari");
        int role = rows[0].ToList().IndexOf("cagri_rolu");
        // The fixture's child workflow is not in the inventory, and the cell says so rather than printing an id.
        Assert.All(rows.Skip(1), row => Assert.Equal("(envanterde bulunamadı)", row[children]));
        Assert.All(rows.Skip(1), row => Assert.Equal("giriş noktası", row[role]));
    }

    /// <summary>
    /// Workflows that came with the product are not this company's to rebuild. The split is CRM's own
    /// <c>ismanaged</c> flag — never a guess from the name, which would be a judgement the tool cannot make.
    /// </summary>
    [Fact]
    public async Task Workflows_Supplied_With_The_Product_Are_Held_Apart_From_The_Work()
    {
        FakeOrganization organization = new() { WorkflowCount = 8, Managed = new HashSet<int> { 6 } };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.True(code == ExitCode.Success, console);

        // It leaves the plan for its counterpart book, with the reason beside it, and keeps its diagram: nothing
        // disappears silently, and nobody has to filter the plan to find the work that is actually theirs.
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.DoesNotContain(plan.Rows("İş akışları"), row => row.Contains("Poliçe İptal Süreci 6"));
        Workbook excluded = Workbook.Open(Path.Combine(runRoot, "raporlar", "kapsam-disi.xlsx"));
        Assert.Equal(["Nasıl okunur", "Kapsam dışı"], excluded.Names);
        IReadOnlyList<string> row6 = Assert.Single(excluded.Rows("Kapsam dışı"), row => row.Contains("Poliçe İptal Süreci 6"));
        Assert.StartsWith("ürünle gelmiş", row6[0], StringComparison.Ordinal);
        // Its diagram is kept, outside the delivered bpmn/ folder, where the out-of-scope book points.
        Assert.True(File.Exists(Path.Combine(runRoot, "kapsam-disi-bpmn", "is-akisi", "new-policy", "police-iptal-sureci-6.bpmn")));
        Assert.False(File.Exists(Path.Combine(runRoot, "bpmn", "is-akislari", "is-akisi", "new-policy", "police-iptal-sureci-6.bpmn")));
        Assert.Equal("kapsam-disi-bpmn/is-akisi/new-policy/police-iptal-sureci-6.bpmn", row6[excluded.Headers("Kapsam dışı").ToList().IndexOf("bpmn_dosyasi")]);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        Assert.All(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— uygun", link.GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Every_Diagram_Carries_A_Header_Note_Above_The_Flow()
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor("condition-update-stop.xaml")), "test");

        XElement note = Assert.Single(xml.Descendants(BpmnSerializer.Model + "textAnnotation"));
        string text = note.Element(BpmnSerializer.Model + "text")!.Value;
        Assert.Contains("Poliçe İptal", text, StringComparison.Ordinal);
        Assert.Contains("İş Akışı · Arka plan · Etkin · kayıt türü: new_policy", text, StringComparison.Ordinal);
        Assert.Contains("Şununla başlar:", text, StringComparison.Ordinal);
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

    /// <summary>
    /// The diagram is what an analyst opens most, so what the rest of the run learned about a workflow is on its
    /// face: whether anything else calls it, whether it is one of many like it, whether it is known to run, and
    /// whether what runs in CRM is actually what is drawn.
    /// </summary>
    [Fact]
    public void The_Header_Note_Carries_What_The_Rest_Of_The_Run_Knows()
    {
        Crm.Ir.Model.WorkflowIr ir = BpmnEmissionTests.IrFor("condition-update-stop.xaml");
        DiagramFacts facts = new("yapı taşı — 3 iş akışı bunu çağırıyor, tek başına taşınmaz",
            "Kullanılıyor: son kayıtlı çalışma 2026-09-20 (sistem işi)", RunningCopyDiffers: true);

        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(ir, null, facts), "test");

        string note = xml.Descendants(BpmnSerializer.Model + "textAnnotation").Single().Element(BpmnSerializer.Model + "text")!.Value;
        Assert.Contains("Rol: yapı taşı — 3 iş akışı bunu çağırıyor", note, StringComparison.Ordinal);
        // Families were grouping by resemblance, and answered the wrong question (maintainer, 2026-10-09): how a
        // workflow is used is in the stage that fires it. No diagram names one any more.
        Assert.DoesNotContain("Aile", note, StringComparison.Ordinal);
        Assert.Contains("Kullanım: Kullanılıyor: son kayıtlı çalışma 2026-09-20", note, StringComparison.Ordinal);
        Assert.Contains("UYARI: CRM'de çalışan kopya bu tanımdan farklı", note, StringComparison.Ordinal);

        // The box has to be tall enough for the lines a reader will see, not only for the newlines in the text.
        double height = double.Parse(xml.Descendants(BpmnSerializer.Di + "BPMNShape")
            .Single(shape => shape.Attribute("bpmnElement")!.Value.StartsWith("note_", StringComparison.Ordinal))
            .Element(BpmnSerializer.Dc + "Bounds")!.Attribute("height")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(height >= note.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / 80.0))) * 15);
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
