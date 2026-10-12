using System.Text.Json.Nodes;
using Crm.Cli;
using Crm.Cli.Stages;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The process-mining report: the totals the browser reduced, turned into an internal workbook and internal copies of
/// the stage maps with the traffic on them — and none of it anywhere near the delivered package.
/// </summary>
public sealed class ProcessMiningTests
{
    private static string Totals(TemporaryOutput output, bool found = true)
    {
        JsonObject totals = new()
        {
            ["format"] = ProcessMiningStage.Format,
            ["exportedAtUtc"] = "2026-10-12T09:00:00Z",
            ["source"] = found
                ? new JsonObject { ["entity"] = "ps_stephistory", ["caseAttribute"] = "ps_caseid", ["stageAttribute"] = "ps_stepid", ["chosenBy"] = "tek aday" }
                : null,
            ["candidates"] = new JsonArray(new JsonObject { ["entity"] = "ps_stephistory", ["caseAttribute"] = "ps_caseid", ["stageAttribute"] = "ps_stepid", ["rows"] = 5000 }),
            ["minimumGroup"] = 5,
            ["window"] = new JsonObject { ["fromDate"] = "2025-01-02", ["toDate"] = "2026-10-11" },
            ["cases"] = 9,
            ["events"] = 21,
            ["skipped"] = 1,
            ["stages"] = new JsonArray(
                new JsonObject { ["stage"] = StageMapStageTests.Control, ["entries"] = 8, ["lastRecorded"] = 0 },
                new JsonObject { ["stage"] = StageMapStageTests.Nova, ["entries"] = 6, ["lastRecorded"] = 1 },
                new JsonObject { ["stage"] = StageMapStageTests.Waiting, ["entries"] = 2, ["lastRecorded"] = 0 }),
            ["transitions"] = new JsonArray(
                new JsonObject { ["from"] = StageMapStageTests.Control, ["to"] = StageMapStageTests.Nova, ["count"] = 6, ["meanHours"] = 9.5, ["medianHours"] = 7, ["p90Hours"] = 20 },
                new JsonObject { ["from"] = StageMapStageTests.Control, ["to"] = StageMapStageTests.Waiting, ["count"] = 2 },
                new JsonObject { ["from"] = StageMapStageTests.Nova, ["to"] = StageMapStageTests.Waiting, ["count"] = 5, ["meanHours"] = 60, ["medianHours"] = 72, ["p90Hours"] = 90 })
        };
        Directory.CreateDirectory(output.Root);
        string file = Path.Combine(output.Root, "crm-surec.json");
        File.WriteAllText(file, totals.ToJsonString());
        return file;
    }

    [Fact]
    public async Task The_Totals_Become_An_Internal_Workbook()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await StageMapStageTests.RunAsync(output, withStages: true, processMiningFile: Totals(output));

        Assert.True(code == ExitCode.Success, console);
        Workbook book = Workbook.Open(Path.Combine(runRoot, "raporlar", "surec-madenciligi.xlsx"));
        Assert.Equal(["Nasıl okunur", "Aşama girişleri", "Geçişler"], book.Names);
        IReadOnlyList<IReadOnlyList<string>> steps = book.Rows("Geçişler");
        List<string> headers = [.. steps[0]];
        // The outcome is read off the stage record: the history says where a case went, the record on which outcome.
        IReadOnlyList<string> success = Assert.Single(steps, row => row[headers.IndexOf("hedef_asama")] == "TALEBİ NOVAYA GÖNDER");
        Assert.Equal("olumlu", success[headers.IndexOf("sonuc")]);
        Assert.Equal("6", success[headers.IndexOf("gecis_sayisi")]);
        Assert.Equal("7", success[headers.IndexOf("medyan_saat")]);
        // A rare step has its count and no durations.
        IReadOnlyList<string> rare = Assert.Single(steps, row => row[headers.IndexOf("kaynak_asama")] == "CRM EVRAK KONTROL"
            && row[headers.IndexOf("hedef_asama")] == "EVRAK BEKLENİYOR");
        Assert.Equal("olumsuz", rare[headers.IndexOf("sonuc")]);
        Assert.Equal("", rare[headers.IndexOf("medyan_saat")]);
        // A step no outcome of the stage leads to happened anyway, and says so.
        Assert.Contains(steps, row => row[headers.IndexOf("kaynak_asama")] == "TALEBİ NOVAYA GÖNDER" && row[headers.IndexOf("sonuc")] == "tasarımda yok");
        Assert.Contains(book.Rows("Nasıl okunur"), row => row.Count > 1 && row[1].Contains("2025-01-02 – 2026-10-11", StringComparison.Ordinal));
    }

    /// <summary>
    /// The traffic is drawn on COPIES of the maps, in the internal folder. The delivered maps, the delivered workbooks and
    /// the guide stay exactly as they were and never name the report.
    /// </summary>
    [Fact]
    public async Task The_Traffic_Is_On_Internal_Copies_Of_The_Maps_And_Nowhere_Delivered()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await StageMapStageTests.RunAsync(output, withStages: true, processMiningFile: Totals(output));

        Assert.True(code == ExitCode.Success, console);
        const string Map = "dt-maas-degisikligi--crm-evrak-kontrol.bpmn";
        string internalMap = File.ReadAllText(Path.Combine(runRoot, "surec-madenciligi-bpmn", Map));
        Assert.Contains("olumlu · 6 kez · medyan 7 sa", internalMap, StringComparison.Ordinal);
        Assert.Contains("CRM EVRAK KONTROL · 8 giriş", internalMap, StringComparison.Ordinal);

        string delivered = File.ReadAllText(Path.Combine(runRoot, "bpmn", "talep-asamalari", Map));
        Assert.DoesNotContain(" kez", delivered, StringComparison.Ordinal);
        Assert.DoesNotContain("giriş\"", delivered, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.xlsx", SearchOption.AllDirectories));

        string guide = AnalystGuideTests.Text(Path.Combine(runRoot, "raporlar", "nasil-kullanilir.docx"));
        Assert.DoesNotContain("surec-madenciligi", guide, StringComparison.OrdinalIgnoreCase);
        foreach (string file in new[] { "tasima-plani.xlsx", "veri-analizi.xlsx", "dis-sistemler.xlsx" })
        {
            Workbook book = Workbook.Open(Path.Combine(runRoot, "raporlar", file));
            Assert.DoesNotContain(book.Names.SelectMany(book.Rows).SelectMany(row => row),
                cell => cell.Contains("surec-madenciligi", StringComparison.OrdinalIgnoreCase) || cell.Contains(" kez · medyan", StringComparison.Ordinal));
        }
        // The internal report names it, and says it stays inside.
        Assert.Contains("İÇ KULLANIM", File.ReadAllText(Path.Combine(runRoot, "raporlar", "rapor.md")), StringComparison.Ordinal);
    }

    /// <summary>When nothing in CRM holds a stage history, the book says so and lists what was looked at; no map is drawn.</summary>
    [Fact]
    public async Task Without_A_History_The_Book_Says_So()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await StageMapStageTests.RunAsync(output, withStages: true, processMiningFile: Totals(output, found: false));

        Assert.True(code == ExitCode.Success, console);
        Workbook book = Workbook.Open(Path.Combine(runRoot, "raporlar", "surec-madenciligi.xlsx"));
        Assert.Contains(book.Rows("Nasıl okunur"), row => row.Count > 1 && row[1].Contains("bulunamadı", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(runRoot, "surec-madenciligi-bpmn")));
    }

    [Fact]
    public void A_File_That_Is_Not_A_Totals_File_Is_Refused()
    {
        Assert.Throws<InvalidDataException>(() => ProcessMiningStage.Parse("{\"format\":\"crm-usage-export/1\"}"));
    }

    [Theory]
    [InlineData(7, "7 sa")]
    [InlineData(47.5, "47,5 sa")]
    [InlineData(72, "3 gün")]
    [InlineData(84, "3,5 gün")]
    public void A_Duration_Reads_In_Hours_Then_Days(double hours, string expected)
    {
        Assert.Equal(expected, ProcessMiningStage.Duration(hours));
    }
}
