using System.Text.Json;
using Crm.Cli;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class EndToEndTests
{
    /// <summary>The M6 gate, against the fake server: one command, a complete run folder, every count accounted for.</summary>
    [Fact]
    public async Task A_Clean_Run_Produces_A_Complete_Folder_Whose_Count_Chain_Balances()
    {
        string update = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        string custom = XamlWorkflowParserTests.Fixture("child-and-custom.xaml");
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 12, NonDesigner = new HashSet<int> { 10 }, XamlFor = (index, _) => index < 6 ? update : custom }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.True(code == ExitCode.Success, console);
        foreach (string file in new[] { "manifest.json", "ham/is-akislari.jsonl", "ham/xaml/dizin.json", "raporlar/rapor.md", "raporlar/ayristirma-kapsami.md",
            "raporlar/sapma.md", "raporlar/birlestirme.md", "raporlar/hassas-degerler.md", "raporlar/envanter.md", "aileler/aileler.csv",
            "aileler/ciftler.csv", "aileler/aileler.json", "elle-inceleme/dizin.md", "gunlukler/calistirma.log" })
        {
            Assert.True(File.Exists(Path.Combine(runRoot, file)), file + " is missing");
        }
        Assert.Equal(5, Directory.GetFiles(Path.Combine(runRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        List<string> chain = [.. manifest.RootElement.GetProperty("countChain").EnumerateArray().Select(link => link.GetString()!)];
        Assert.Equal(["kayıtlar", "sınıflandırma", "xaml", "ara model", "bpmn", "aileler"], chain.Select(link => link[..link.IndexOf(':', StringComparison.Ordinal)]));
        Assert.All(chain, link => Assert.EndsWith("— uygun", link, StringComparison.Ordinal));
        Assert.Contains("Sayım zinciri (§8):", console, StringComparison.Ordinal);
        Assert.Contains("ara model: tanım xaml dosyası 6 = ara model + elle inceleme + ayrıştırma hatası + karşılıksız xaml 6 — uygun", console, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Stage_Count_That_Does_Not_Balance_Is_Reported_As_A_Gap()
    {
        RunState state = new("x", "x", "x");
        state.Counts["ir.documents"] = 3;
        state.Counts["bpmn.written"] = 2;

        CountLink bpmn = Assert.Single(CountChain.Evaluate(state), link => link.Name == "bpmn");

        Assert.False(bpmn.Holds);
        Assert.EndsWith("EKSİK: 1", bpmn.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The parser-improvement loop: a run taken at the company is reprocessed elsewhere with no CRM at all.
    /// The fake server is not even reachable in the second run — any request would fail the test.
    /// </summary>
    [Fact]
    public async Task Reprocessing_An_Earlier_Run_Rebuilds_Every_Offline_Output_Without_The_Network()
    {
        string update = XamlWorkflowParserTests.Fixture("condition-update-stop.xaml");
        using TemporaryOutput output = new();
        (_, string firstRoot, _) = await RunHarness.RunAsync(new FakeOrganization { WorkflowCount = 6, XamlFor = (_, _) => update }.Build(), output);
        FakeCrmServer silent = new();

        (ExitCode code, string secondRoot, string console) = await RunHarness.RunAsync(silent, output, reprocessRunId: Path.GetFileName(firstRoot));

        Assert.True(code == ExitCode.Success, console);
        Assert.Empty(silent.Requests);
        Assert.NotEqual(firstRoot, secondRoot);
        Assert.Equal(Directory.GetFiles(Path.Combine(firstRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Select(Path.GetFileName), Directory.GetFiles(Path.Combine(secondRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Select(Path.GetFileName));
        Assert.Equal(File.ReadAllBytes(Path.Combine(firstRoot, "ham", "is-akislari.jsonl")), File.ReadAllBytes(Path.Combine(secondRoot, "ham", "is-akislari.jsonl")));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(secondRoot, RunFolder.ManifestFileName)));
        Assert.Contains("yeniden işleme:" + Path.GetFileName(firstRoot), manifest.RootElement.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.All(manifest.RootElement.GetProperty("countChain").EnumerateArray(), link => Assert.EndsWith("— uygun", link.GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reprocessing_A_Run_That_Does_Not_Exist_Fails_Cleanly()
    {
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, reprocessRunId: "19990101-000000");

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("mühürlenmiş bir çalıştırma değil", console, StringComparison.Ordinal);
    }
}
