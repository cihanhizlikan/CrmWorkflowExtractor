using System.Text.Json.Nodes;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Cli;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// From an export carrying the stage records to a stage map and the plan's first sheet. The rows are in the shape
/// the Web API answers with — a lookup as <c>_x_value</c> beside its formatted name, an option as its number beside
/// its label — and point at workflows the mock export really has, so the embedding is of a real diagram.
/// </summary>
public sealed class StageMapStageTests
{
    private const string Control = "10000000-0000-0000-0000-000000000001";
    private const string Nova = "10000000-0000-0000-0000-000000000002";
    private const string Waiting = "10000000-0000-0000-0000-000000000003";
    private const string Old = "10000000-0000-0000-0000-000000000004";
    private const string Category = "20000000-0000-0000-0000-000000000001";
    private const string Workflow = "00000000-0000-0000-0000-000000000002";
    private const string User = "30000000-0000-0000-0000-000000000001";
    private const string Supplied = "00000000-0000-0000-0000-000000000004";
    private const string Draft = "00000000-0000-0000-0000-000000000006";

    private static JsonObject Row(string id, string shortName, bool primary, int state)
    {
        return new JsonObject
        {
            ["ps_stepid"] = id,
            ["ps_name"] = "DT – Maaş Değişikliği | " + shortName,
            ["ps_stepname"] = shortName,
            ["_ps_categoryid_value"] = Category,
            ["_ps_categoryid_value@OData.Community.Display.V1.FormattedValue"] = "DT – Maaş Değişikliği",
            ["ps_primarystep"] = primary,
            ["ps_primarystep@OData.Community.Display.V1.FormattedValue"] = primary ? "Evet" : "Hayır",
            ["ps_isautostep"] = false,
            ["statecode"] = state,
            ["statecode@OData.Community.Display.V1.FormattedValue"] = state == 0 ? "Etkin" : "Devre Dışı",
            ["ps_slaperiod"] = 2,
            ["ps_slaperiod@OData.Community.Display.V1.FormattedValue"] = "2"
        };
    }

    private static void Link(JsonObject row, string suffix, string? next, string? nextName, string? workflow, string? workflowName)
    {
        if (next is not null)
        {
            row[$"_ps_nextstepfor{suffix}id_value"] = next;
            row[$"_ps_nextstepfor{suffix}id_value@OData.Community.Display.V1.FormattedValue"] = nextName;
        }
        if (workflow is not null)
        {
            row[$"_ps_wffor{suffix}id_value"] = workflow;
            row[$"_ps_wffor{suffix}id_value@OData.Community.Display.V1.FormattedValue"] = workflowName;
        }
    }

    /// <param name="output">Where the run goes.</param>
    /// <param name="withStages">Whether the export carries the stage records at all.</param>
    /// <param name="outOfScope">
    /// Whether two more stages fire workflows the plan would otherwise leave out: one CRM reports as product-supplied,
    /// and a draft.
    /// </param>
    private static async Task<(ExitCode Code, string RunRoot, string Console)> RunAsync(TemporaryOutput output, bool withStages, bool outOfScope = false)
    {
        JsonNode export = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json")))!;
        if (outOfScope)
        {
            JsonObject supplied = export["workflows"]!.AsArray().OfType<JsonObject>().Single(workflow => workflow["workflowid"]!.GetValue<string>() == Supplied);
            supplied["ismanaged"] = true;
        }
        if (withStages)
        {
            JsonObject control = Row(Control, "CRM EVRAK KONTROL", true, 0);
            Link(control, "success", Nova, "DT – Maaş Değişikliği | TALEBİ NOVAYA GÖNDER", Workflow, "Poliçe İptal Süreci 1");
            Link(control, "unsuccess", Waiting, "DT – Maaş Değişikliği | EVRAK BEKLENİYOR", null, null);
            control["ps_smstextforsuccess"] = "Talebiniz alındı.";
            control["_ps_systemuserid_value"] = User;
            control["_ps_systemuserid_value@OData.Community.Display.V1.FormattedValue"] = "Ayşe Yılmaz";
            control["ps_iscallback"] = true;
            control["ps_iscallback@OData.Community.Display.V1.FormattedValue"] = "Evet";
            JsonObject nova = Row(Nova, "TALEBİ NOVAYA GÖNDER", false, 0);
            Link(nova, "unsuccess", Old, "DT – Maaş Değişikliği | ESKİ KONTROL", null, null);
            JsonObject waiting = Row(Waiting, "EVRAK BEKLENİYOR", false, 0);
            Link(waiting, "success", Control, "DT – Maaş Değişikliği | CRM EVRAK KONTROL", null, null);
            if (outOfScope)
            {
                Link(nova, "success", null, null, Supplied, "Poliçe İptal Süreci 2");
                Link(waiting, "unsuccess", null, null, Draft, "Hasar Onay 6");
            }
            JsonObject old = Row(Old, "ESKİ KONTROL", false, 1);
            export["caseStages"] = new JsonArray(control, nova, waiting, old);
        }
        Directory.CreateDirectory(output.Root);
        string file = Path.Combine(output.Root, "with-stages.json");
        File.WriteAllText(file, export.ToJsonString());
        return await RunHarness.ImportAsync(output, file);
    }

    [Fact]
    public async Task An_Active_Primary_Stage_Becomes_A_Map_With_Its_Workflow_Inside()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true);

        Assert.True(code == ExitCode.Success, console);
        string map = Assert.Single(Directory.GetFiles(Path.Combine(runRoot, "bpmn", "talep-asamalari"), "*.bpmn"));
        // Named for what a reader holds: the request subject and the stage it starts at.
        Assert.Equal("dt-maas-degisikligi--crm-evrak-kontrol.bpmn", Path.GetFileName(map));
        XDocument xml = XDocument.Load(map);
        Assert.Empty(BpmnSchemaValidator.Validate(xml));
        XElement process = xml.Root!.Element(BpmnSerializer.Model + "process")!;
        Assert.Equal("DT – Maaş Değişikliği", process.Attribute("name")!.Value);
        // The workflow fired on success, with the real workflow's steps inside it.
        XElement embedded = process.Elements(BpmnSerializer.Model + "subProcess").Single();
        Assert.Equal("Poliçe İptal Süreci 1", embedded.Attribute("name")!.Value);
        Assert.NotEmpty(embedded.Elements(BpmnSerializer.Model + "startEvent"));
        // A stage that has been switched off is still drawn where a case would reach it, and says so.
        Assert.Contains(process.Elements(BpmnSerializer.Model + "userTask"), task => task.Attribute("name")!.Value == "ESKİ KONTROL (pasif)");
    }

    /// <summary>
    /// The process first, the workflows second. Rows in the order a case meets the stages, a switched-off stage
    /// left out of the work but named where an outcome still leads to it — and nobody's name anywhere in a
    /// workbook that goes to an outsource partner.
    /// </summary>
    [Fact]
    public async Task The_Plan_Opens_With_The_Stages()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true);

        Assert.True(code == ExitCode.Success, console);
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Equal(["Nasıl okunur", "Talep konuları", "Talep aşamaları", "İş akışları"], plan.Names.Take(4));
        IReadOnlyList<IReadOnlyList<string>> rows = plan.Rows("Talep aşamaları");
        List<string> headers = [.. rows[0]];
        Assert.Equal(["CRM EVRAK KONTROL", "TALEBİ NOVAYA GÖNDER", "EVRAK BEKLENİYOR"], rows.Skip(1).Select(row => row[headers.IndexOf("haritadaki_ad")]));
        IReadOnlyList<string> control = rows[1];
        Assert.Equal("DT – Maaş Değişikliği | TALEBİ NOVAYA GÖNDER", control[headers.IndexOf("olumlu_sonraki_asama")]);
        Assert.Equal("Poliçe İptal Süreci 1", control[headers.IndexOf("olumlu_is_akisi")]);
        Assert.Equal("Talebiniz alındı.", control[headers.IndexOf("olumlu_sms")]);
        Assert.Equal("evet", control[headers.IndexOf("kullaniciya_atanir")]);
        Assert.Equal("2", control[headers.IndexOf("sla_periyodu")]);
        Assert.Equal("bpmn/talep-asamalari/dt-maas-degisikligi--crm-evrak-kontrol.bpmn", control[headers.IndexOf("asama_haritasi")]);
        Assert.Equal("DT – Maaş Değişikliği | ESKİ KONTROL (pasif)", rows[2][headers.IndexOf("olumsuz_sonraki_asama")]);
        // A rule a reader acts on has a column; the rest share one cell, named as CRM labels them.
        Assert.DoesNotContain("geri_donus", headers);
        Assert.Equal("Geri dönüş mü: Evet", control[headers.IndexOf("diger_kurallar")]);
        Assert.DoesNotContain(plan.Names.SelectMany(plan.Rows).SelectMany(row => row), cell => cell.Contains("Ayşe", StringComparison.Ordinal));
    }

    /// <summary>The index of the maps: one row per request subject, how big its process is, and the file to open.</summary>
    [Fact]
    public async Task The_Plan_Indexes_The_Maps_By_Request_Subject()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true);

        Assert.True(code == ExitCode.Success, console);
        IReadOnlyList<IReadOnlyList<string>> rows = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx")).Rows("Talep konuları");
        Assert.Equal(["talep_konusu", "baslangic_asamasi", "asama_sayisi", "is_akisi_sayisi", "is_akislari", "toplam_adim", "asama_haritasi"], rows[0]);
        IReadOnlyList<string> topic = Assert.Single(rows.Skip(1));
        Assert.Equal(["DT – Maaş Değişikliği", "CRM EVRAK KONTROL", "4", "1", "Poliçe İptal Süreci 1"], topic.Take(5));
        Assert.Equal("bpmn/talep-asamalari/dt-maas-degisikligi--crm-evrak-kontrol.bpmn", topic[6]);
    }

    /// <summary>
    /// The stages point to the workflows, and now the workflows point back: on the row, as the stages that fire it,
    /// and on the workflow's own diagram, with the map those stages are drawn on.
    /// </summary>
    [Fact]
    public async Task A_Workflow_A_Stage_Fires_Says_So_On_Its_Row_And_Its_Diagram()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true);

        Assert.True(code == ExitCode.Success, console);
        IReadOnlyList<IReadOnlyList<string>> rows = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx")).Rows("İş akışları");
        List<string> headers = [.. rows[0]];
        IReadOnlyList<string> fired = Assert.Single(rows, row => row[0] == "Poliçe İptal Süreci 1");
        Assert.Equal("DT – Maaş Değişikliği › CRM EVRAK KONTROL (olumlu)", fired[headers.IndexOf("calistiran_asamalar")]);
        Assert.Equal("aşama adımı", fired[headers.IndexOf("cagri_rolu")]);
        Assert.All(rows.Skip(1).Where(row => row != fired), row => Assert.Equal("", row[headers.IndexOf("calistiran_asamalar")]));

        string diagram = File.ReadAllText(Path.Combine(runRoot, fired[headers.IndexOf("bpmn_dosyasi")].Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("Aşamalardan çalışır: DT – Maaş Değişikliği › CRM EVRAK KONTROL (olumlu) — bpmn/talep-asamalari/dt-maas-degisikligi--crm-evrak-kontrol.bpmn",
            diagram, StringComparison.Ordinal);
        Assert.Contains("Rol: aşama adımı", diagram, StringComparison.Ordinal);
    }

    /// <summary>
    /// A workflow a live stage fires is part of the process, so it is in the plan even when CRM reports it as
    /// product-supplied — a map that embeds a workflow the plan left out sends the reader to a diagram they were told
    /// not to need. A draft still stays out: a draft cannot run, whatever fires it. And the map, which is delivered,
    /// never names the internal folder the draft's own diagram is kept in.
    /// </summary>
    [Fact]
    public async Task A_Workflow_A_Live_Stage_Fires_Is_In_The_Plan_Unless_It_Is_A_Draft()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true, outOfScope: true);

        Assert.True(code == ExitCode.Success, console);
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Workbook excluded = Workbook.Open(Path.Combine(runRoot, "raporlar", "kapsam-disi.xlsx"));
        Assert.Contains(plan.Rows("İş akışları"), row => row[0] == "Poliçe İptal Süreci 2");
        Assert.DoesNotContain(excluded.Rows("Kapsam dışı"), row => row.Contains("Poliçe İptal Süreci 2"));
        Assert.Contains(excluded.Rows("Kapsam dışı"), row => row.Contains("Hasar Onay 6"));
        Assert.DoesNotContain(plan.Rows("İş akışları"), row => row[0] == "Hasar Onay 6");

        string map = File.ReadAllText(Path.Combine(runRoot, "bpmn", "talep-asamalari", "dt-maas-degisikligi--crm-evrak-kontrol.bpmn"));
        Assert.Contains("name=\"Hasar Onay 6\"", map, StringComparison.Ordinal);
        Assert.DoesNotContain("kapsam-disi", map, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guide teaches the process before the workflows, on a real map of the run: the reader who has never seen
    /// this CRM is walked through a request subject first, and told which map file it is.
    /// </summary>
    [Fact]
    public async Task The_Guide_Walks_A_Real_Map_Before_A_Workflow()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true);

        Assert.True(code == ExitCode.Success, console);
        string text = AnalystGuideTests.Text(Path.Combine(runRoot, "raporlar", "nasil-kullanilir.docx"));
        int stages = text.IndexOf("3  Bir talep konusunu adım adım çözümleme", StringComparison.Ordinal);
        int workflow = text.IndexOf("4  Bir iş akışını adım adım çözümleme", StringComparison.Ordinal);
        Assert.True(stages > 0 && workflow > stages, "Aşama bölümü iş akışı bölümünden önce gelmeli.");
        Assert.Contains("bpmn/talep-asamalari/dt-maas-degisikligi--crm-evrak-kontrol.bpmn", text, StringComparison.Ordinal);
        Assert.Contains("Talep konuları", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Taşıma planı", text, StringComparison.Ordinal);
    }

    /// <summary>An export made before the stages were collected still runs, and opens on the workflows as before.</summary>
    [Fact]
    public async Task Without_The_Stages_The_Plan_Opens_On_The_Workflows_And_Says_Why()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: false);

        Assert.True(code == ExitCode.Success, console);
        Assert.False(Directory.Exists(Path.Combine(runRoot, "bpmn", "talep-asamalari")));
        Assert.Equal(["Nasıl okunur", "İş akışları"], Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx")).Names.Take(2));
        Assert.Contains("talep aşamaları (ps_step) yok", console, StringComparison.Ordinal);
    }
}
