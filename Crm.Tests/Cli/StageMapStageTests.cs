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

    private static async Task<(ExitCode Code, string RunRoot, string Console)> RunAsync(TemporaryOutput output, bool withStages)
    {
        JsonNode export = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json")))!;
        if (withStages)
        {
            JsonObject control = Row(Control, "CRM EVRAK KONTROL", true, 0);
            Link(control, "success", Nova, "DT – Maaş Değişikliği | TALEBİ NOVAYA GÖNDER", Workflow, "Poliçe İptal Süreci 1");
            Link(control, "unsuccess", Waiting, "DT – Maaş Değişikliği | EVRAK BEKLENİYOR", null, null);
            control["ps_smstextforsuccess"] = "Talebiniz alındı.";
            control["_ps_systemuserid_value"] = User;
            control["_ps_systemuserid_value@OData.Community.Display.V1.FormattedValue"] = "Ayşe Yılmaz";
            JsonObject nova = Row(Nova, "TALEBİ NOVAYA GÖNDER", false, 0);
            Link(nova, "unsuccess", Old, "DT – Maaş Değişikliği | ESKİ KONTROL", null, null);
            JsonObject waiting = Row(Waiting, "EVRAK BEKLENİYOR", false, 0);
            Link(waiting, "success", Control, "DT – Maaş Değişikliği | CRM EVRAK KONTROL", null, null);
            JsonObject old = Row(Old, "ESKİ KONTROL", false, 1);
            export["caseStages"] = new JsonArray(control, nova, waiting, old);
        }
        Directory.CreateDirectory(output.Root);
        string file = Path.Combine(output.Root, "with-stages.json");
        File.WriteAllText(file, export.ToJsonString());
        return await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: file);
    }

    [Fact]
    public async Task An_Active_Primary_Stage_Becomes_A_Map_With_Its_Workflow_Inside()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: true);

        Assert.True(code == ExitCode.Success, console);
        string map = Assert.Single(Directory.GetFiles(Path.Combine(runRoot, "bpmn", "asama-akislari"), "*.bpmn"));
        Assert.Equal("dt-maas-degisikligi.bpmn", Path.GetFileName(map));
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
        Assert.Equal(["Nasıl okunur", "Aşamalar", "Taşıma planı"], plan.Names.Take(3));
        IReadOnlyList<IReadOnlyList<string>> rows = plan.Rows("Aşamalar");
        List<string> headers = [.. rows[0]];
        Assert.Equal(["CRM EVRAK KONTROL", "TALEBİ NOVAYA GÖNDER", "EVRAK BEKLENİYOR"], rows.Skip(1).Select(row => row[headers.IndexOf("kisa_ad")]));
        IReadOnlyList<string> control = rows[1];
        Assert.Equal("DT – Maaş Değişikliği | TALEBİ NOVAYA GÖNDER", control[headers.IndexOf("olumlu_sonraki_asama")]);
        Assert.Equal("Poliçe İptal Süreci 1", control[headers.IndexOf("olumlu_is_akisi")]);
        Assert.Equal("Talebiniz alındı.", control[headers.IndexOf("olumlu_sms")]);
        Assert.Equal("evet", control[headers.IndexOf("kullaniciya_atanir")]);
        Assert.Equal("2", control[headers.IndexOf("sla_periyodu")]);
        Assert.Equal("bpmn/asama-akislari/dt-maas-degisikligi.bpmn", control[headers.IndexOf("asama_akisi_bpmn")]);
        Assert.Equal("DT – Maaş Değişikliği | ESKİ KONTROL (pasif)", rows[2][headers.IndexOf("olumsuz_sonraki_asama")]);
        Assert.DoesNotContain(plan.Names.SelectMany(plan.Rows).SelectMany(row => row), cell => cell.Contains("Ayşe", StringComparison.Ordinal));
    }

    /// <summary>An export made before the stages were collected still runs, and opens on the workflows as before.</summary>
    [Fact]
    public async Task Without_The_Stages_The_Plan_Opens_On_The_Workflows_And_Says_Why()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunAsync(output, withStages: false);

        Assert.True(code == ExitCode.Success, console);
        Assert.False(Directory.Exists(Path.Combine(runRoot, "bpmn", "asama-akislari")));
        Assert.Equal(["Nasıl okunur", "Taşıma planı"], Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx")).Names.Take(2));
        Assert.Contains("talep aşamaları (ps_step) yok", console, StringComparison.Ordinal);
    }
}
