using Crm.Cli;
using Crm.Cli.Stages;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Similarity;
using Crm.Tests.Bpmn;
using Crm.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// One workflow may not cost the run. The BPMN stage runs before consolidation and every report, so anything
/// thrown while drawing one diagram used to take the workbooks and the guide with it — and the thing that threw
/// was a single stray byte in a single name.
/// </summary>
public sealed class BpmnStageGuardTests
{
    private static readonly Guid Good = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid Broken = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid AlsoGood = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    /// <summary>
    /// Two steps sharing one path is a malformed intermediate model — a parser fault, not a data one — and
    /// <c>FlowGraph</c> refuses it with "Duplicate BPMN element id". It stands here for the whole class of thing
    /// nobody anticipated, which is what the guard is for.
    /// </summary>
    private static WorkflowIr Malformed(WorkflowIr sound)
    {
        StepNode step = sound.Steps[0];
        return sound with { Steps = [step, step with { }] };
    }

    private static WorkflowIr Named(Guid id, string name)
    {
        WorkflowIr ir = BpmnEmissionTests.IrFor("condition-update-stop.xaml", id);
        return ir with { Identity = ir.Identity with { Name = name } };
    }

    /// <summary>
    /// A diagram's folder is <c>bpmn/&lt;kategori&gt;/&lt;birincil varlık&gt;/</c>, and an entity whose logical name
    /// is one Windows keeps for a device would make a folder that cannot be created — measured on Windows 11,
    /// where <c>mkdir nul</c> throws while <c>nul.bpmn</c> writes fine. With the guard in place the cost would be
    /// only that diagram, quietly; the point of this test is that there is no cost at all.
    /// </summary>
    [Fact]
    public async Task An_Entity_Named_For_A_Device_Still_Gets_Its_Diagram()
    {
        using TemporaryOutput output = new();
        RunFolder folder = RunFolder.Create(output.Root, DateTimeOffset.UtcNow);
        RunState state = new(folder.RunId, folder.Root, "test");
        WorkflowIr sound = Named(Good, "Poliçe İptal");
        List<WorkflowIr> documents = [sound with { Identity = sound.Identity with { PrimaryEntity = "nul" } }];

        await BpmnStage.RunAsync(folder, state, documents, new SimilarityResult([], []), null, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(state.Failures);
        Assert.Equal(0, state.Counts["bpmn.lost"]);
        string file = Assert.Single(Directory.GetFiles(Path.Combine(folder.Root, "bpmn"), "*.bpmn", SearchOption.AllDirectories));
        Assert.Contains("nul-ayrilmis", file, StringComparison.Ordinal);
        Assert.NotEmpty(File.ReadAllBytes(file));
    }

    [Fact]
    public async Task A_Diagram_That_Cannot_Be_Drawn_Costs_That_Diagram_And_Nothing_Else()
    {
        using TemporaryOutput output = new();
        RunFolder folder = RunFolder.Create(output.Root, DateTimeOffset.UtcNow);
        RunState state = new(folder.RunId, folder.Root, "test");
        List<WorkflowIr> documents =
        [
            Named(Good, "Poliçe İptal"),
            Malformed(Named(Broken, "Bozuk Akış")),
            Named(AlsoGood, "Teklif Onay")
        ];

        await BpmnStage.RunAsync(folder, state, documents, new SimilarityResult([], []), null, NullLogger.Instance, CancellationToken.None);

        // The two sound ones are drawn; the broken one is not, and the stage returned rather than throwing.
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder.Root, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length);
        Assert.Equal(2, state.Counts["bpmn.written"]);
        Assert.Equal(1, state.Counts["bpmn.lost"]);

        // The run is marked failed and the failure names the workflow a human has to go and look at.
        Assert.Equal(ExitCode.RunFailed, state.ExitCode);
        string failure = Assert.Single(state.Failures);
        Assert.Contains("Bozuk Akış", failure, StringComparison.Ordinal);
        Assert.Contains("çizilemedi", failure, StringComparison.Ordinal);

        // Nothing may point at a file that was never written: the plan's bpmn_dosyasi cell stays empty.
        Assert.False(state.BpmnFiles.ContainsKey(Broken));
        Assert.True(state.BpmnFiles.ContainsKey(Good));
        Assert.True(state.BpmnFiles.ContainsKey(AlsoGood));
    }

    /// <summary>
    /// A cancellation is the operator's and travels. Swallowing it would turn a stop into 1437 recorded failures
    /// and a run that carried on regardless.
    /// </summary>
    [Fact]
    public async Task A_Cancellation_Is_Not_Mistaken_For_A_Broken_Diagram()
    {
        using TemporaryOutput output = new();
        RunFolder folder = RunFolder.Create(output.Root, DateTimeOffset.UtcNow);
        RunState state = new(folder.RunId, folder.Root, "test");
        List<WorkflowIr> documents = [Named(Good, "Poliçe İptal")];
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BpmnStage.RunAsync(
            folder, state, documents, new SimilarityResult([], []), null, NullLogger.Instance, cancelled.Token));

        Assert.Empty(state.Failures);
    }
}
