using System.Text.Json;
using Crm.Cli;
using Crm.Cli.Stages;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>Usage evidence (tools/crm-usage-export.js) and drafts held apart from grouping, over the synthetic browser fixtures.</summary>
public sealed class UsageStageTests
{
    private static readonly Guid DefinitionThatRan = Guid.Parse("00000000-0000-0000-0000-000000000000");

    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", name);
    }

    [Fact]
    public async Task Usage_Evidence_Is_Kept_Reported_And_Drafts_Are_Held_Apart_From_Grouping()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        Assert.True(code == ExitCode.Success, console);
        Assert.Equal(File.ReadAllBytes(Fixture("mock-usage-export.json")), File.ReadAllBytes(Path.Combine(runRoot, "ham", "kullanim-disa-aktarim.json")));
        Assert.Contains("1 sorgu başarısız oldu", console, StringComparison.Ordinal);

        // The verdict travels in the plan's own row, beside the work it qualifies.
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        IReadOnlyList<IReadOnlyList<string>> work = plan.Rows("Taşıma planı");
        IReadOnlyList<string> ran = Assert.Single(work, row => row.Contains(DefinitionThatRan.ToString("D")));
        Assert.Contains("2026-09-20", ran);
        Assert.Contains("çalışıyor · 2026-09-20", ran);
        Assert.Contains(work, row => row.Contains("kayıtlı çalışma yok"));
        // The short cell is only honest because the caveat is said in full where the columns are explained.
        Assert.Contains(plan.Rows("Nasıl okunur"), row => row.Any(cell => cell.Contains("KANITLAMAZ", StringComparison.Ordinal)));

        // 44 drafts in the fixture, one without XAML (the simulated failure), so 43 IR documents: out of the plan and
        // into its counterpart book with the reason beside them, absent from the families, and the count chain still balances.
        Workbook excluded = Workbook.Open(Path.Combine(runRoot, "raporlar", "kapsam-disi.xlsx"));
        IReadOnlyList<IReadOnlyList<string>> out_ = excluded.Rows("Kapsam dışı");
        Assert.Equal(43, out_.Skip(1).Count(row => row[0].StartsWith("taslak", StringComparison.Ordinal)));
        Assert.All(out_.Skip(1), row => Assert.NotEmpty(row[0]));
        Workbook families = Workbook.Open(Path.Combine(runRoot, "raporlar", "aileler.xlsx"));
        IReadOnlyList<IReadOnlyList<string>> clusters = families.Rows("Aileler");
        Assert.DoesNotContain(clusters, row => row.Any(cell => cell.StartsWith("Hasar Onay", StringComparison.Ordinal)));
        Assert.Contains(clusters, row => row.Contains("2026-09-20"));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        Assert.All(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— uygun", link.GetString(), StringComparison.Ordinal));
        Assert.Contains(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => link.GetString()!.Contains("aileye yerleşen + ayrı tutulan taslak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Reprocessed_Run_Inherits_The_Usage_Evidence_Of_Its_Source_Run()
    {
        using TemporaryOutput output = new();
        (_, string first, _) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Fixture("mock-usage-export.json"));

        (ExitCode code, string second, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, reprocessRunId: Path.GetFileName(first));

        Assert.True(code == ExitCode.Success, console);
        Workbook plan = Workbook.Open(Path.Combine(second, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains(plan.Rows("Taşıma planı"), row => row.Contains("çalışıyor · 2026-09-20"));
    }

    /// <summary>Asking the server for the oldest record of all sorts the whole System Job table; production leaves it pending.</summary>
    [Fact]
    public void The_Export_Script_Never_Sorts_A_Whole_Table()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryTree.Root().FullName, "tools", "crm-usage-export.js"));

        Assert.DoesNotContain("createdon asc", script, StringComparison.Ordinal);
        Assert.Contains("AbortSignal.timeout", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// "The newest run of every activation" is a group-by with a max, and CRM answers it in one GET. Asked record
    /// by record it was eighteen hundred filtered lookups, ten seconds each on the production server — two hours.
    /// The aggregate has a scan limit of its own, so a refusal is expected and must fall back rather than fail.
    /// </summary>
    [Fact]
    public void The_Export_Script_Asks_For_The_Newest_Run_In_One_Aggregate()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryTree.Root().FullName, "tools", "crm-usage-export.js"));

        Assert.Contains("aggregate=\"true\"", script, StringComparison.Ordinal);
        Assert.Contains("aggregate=\"max\"", script, StringComparison.Ordinal);
        Assert.Contains("groupby=\"true\"", script, StringComparison.Ordinal);
        // Refused is not failed: both paths stay, and the export says which one answered.
        Assert.Contains("the aggregate was refused, falling back to one lookup per record", script, StringComparison.Ordinal);
        Assert.Contains("_workflowactivationid_value eq", script, StringComparison.Ordinal);
        Assert.Contains("\"aggregate\" : \"per-record\"", script.Replace(" ? ", " : "), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Missing_Usage_File_Fails_Cleanly()
    {
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Fixture("mock-crm-export.json"), usageFile: Path.Combine(output.Root, "nowhere.json"));

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("Run:UsageFile", console, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DRAFT_OPEN_PHONECALL", true)]
    [InlineData("*draft*case*step", true)]
    [InlineData("18970_DEBUG_TEST", true)]
    [InlineData("Poliçe Deneme Akışı", true)]
    [InlineData("ESKİ - Hasar Onay", true)]
    [InlineData("Poliçe İptal Süreci", false)]
    [InlineData("Contest Kampanya", false)]
    [InlineData("Template Onay", false)]
    public void A_Test_Like_Name_Is_Recognised_As_A_Hint(string name, bool expected)
    {
        Assert.Equal(expected, UsageStage.NameSuggestsTest(name));
    }

    [Fact]
    public void No_Logged_Run_Is_Never_Reported_As_Unused()
    {
        UsageEvidence usage = new(DateTimeOffset.Parse("2026-06-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), null, "", new Dictionary<Guid, WorkflowUsage>());

        Assert.StartsWith("Bilinemez", UsageStage.Verdict(Identity("İş Kuralı", "Arka plan", "Activated"), usage), StringComparison.Ordinal);
        Assert.StartsWith("Hata kaydı yok", UsageStage.Verdict(Identity("İş Akışı", "Gerçek zamanlı", "Etkin"), usage), StringComparison.Ordinal);
        Assert.Contains("KANITI DEĞİLDİR", UsageStage.Verdict(Identity("İş Akışı", "Arka plan", "Etkin"), usage), StringComparison.Ordinal);
        Assert.Contains("KANITI DEĞİLDİR", UsageStage.Verdict(Identity("Diyalog", "Arka plan", "Etkin"), usage), StringComparison.Ordinal);
        Assert.StartsWith("Taslak", UsageStage.Verdict(Identity("İş Akışı", "Arka plan", "Taslak"), usage), StringComparison.Ordinal);
    }

    private static WorkflowIdentity Identity(string category, string mode, string state)
    {
        return new WorkflowIdentity(Guid.NewGuid(), "x", null, category, "Definition", "new_policy", mode, "Organization", state, false, false, null, null, null, 1, true);
    }
}
