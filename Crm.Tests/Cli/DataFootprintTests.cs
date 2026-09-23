using Crm.Cli;
using Crm.Cli.Reports;
using Crm.Ir.Model;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The coupling no diagram shows: a field two workflows write, and a write that starts another workflow because
/// CRM watches that field.
/// </summary>
public sealed class DataFootprintTests
{
    private static readonly Guid Closer = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Stamper = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Watcher = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid Creator = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
    private static readonly Guid OnNewTask = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");

    [Fact]
    public void A_Field_Two_Workflows_Write_Is_Reported_As_Shared()
    {
        IReadOnlyList<FieldUse> fields = DataFootprint.Fields(Organization());

        FieldUse status = Assert.Single(fields, use => use.Field == "statuscode");
        Assert.Equal("incident", status.Entity);
        Assert.Equal([Closer, Stamper], status.Writers);
        Assert.Equal([Watcher], status.TriggeredBy);
    }

    [Fact]
    public void A_Write_That_Starts_Another_Workflow_Is_A_Cascade()
    {
        IReadOnlyList<Cascade> cascades = DataFootprint.Cascades(Organization());

        Assert.Contains(cascades, cascade => cascade.Source == Closer && cascade.Target == Watcher
            && cascade.Through == "incident.statuscode" && cascade.Kind == DataFootprint.CascadeOnUpdate);
        Assert.Contains(cascades, cascade => cascade.Source == Stamper && cascade.Target == Watcher);
        // Creating a record starts whatever watches that entity's creation.
        Assert.Contains(cascades, cascade => cascade.Source == Creator && cascade.Target == OnNewTask
            && cascade.Through == "task" && cascade.Kind == DataFootprint.CascadeOnCreate);
        Assert.DoesNotContain(cascades, cascade => cascade.Source == Watcher);
    }

    [Fact]
    public void A_Workflow_That_Triggers_Itself_Is_Kept_And_Marked()
    {
        WorkflowIr loop = Workflow(Watcher, "Watcher", "incident", ["statuscode"], written: ["incident.statuscode"]);

        IReadOnlyList<Cascade> cascades = DataFootprint.Cascades([loop]);

        Cascade self = Assert.Single(cascades);
        Assert.Equal(self.Source, self.Target);
        Assert.Contains("(kendisi)", DataFootprint.Markdown([loop]), StringComparison.Ordinal);
    }

    [Fact]
    public void The_Report_Counts_Shared_Fields_And_Cascades_Per_Workflow()
    {
        IReadOnlyList<WorkflowIr> documents = Organization();

        (IReadOnlyDictionary<Guid, int> shared, IReadOnlyDictionary<Guid, int> starts) = DataFootprint.PerWorkflow(documents);

        Assert.Equal(1, shared[Closer]);
        Assert.Equal(1, shared[Stamper]);
        Assert.False(shared.ContainsKey(Creator));
        Assert.Equal(1, starts[Closer]);
        Assert.Equal(1, starts[Creator]);

        Sheet cascades = DataFootprint.BuildCascades(documents);
        Assert.Contains(cascades.Rows, row => row.Take(7).Select(cell => Sheet.Cell(cell)?.ToString()).SequenceEqual(
            ["Closer", "Watcher", "alan güncellendi", "incident.statuscode", "hayır", "Arka plan", "Gerçek zamanlı"]));
        Sheet fields = DataFootprint.Build(documents);
        Assert.Contains(fields.Rows, row => Sheet.Cell(row[0])?.ToString() == "incident" && Sheet.Cell(row[1])?.ToString() == "statuscode"
            && Sheet.Cell(row[2]) is 2);
    }

    /// <summary>7009 cascades on the real data (2026-09-22): the list is unreadable, so the page rolls them up.</summary>
    [Fact]
    public void The_Report_Rolls_Cascades_Up_By_Field_And_By_Workflow_Pair()
    {
        string markdown = DataFootprint.Markdown(Organization());

        Assert.Contains("### En çok işi tetikleyen alanlar", markdown, StringComparison.Ordinal);
        Assert.Contains("### İş akışı çiftleri (3 çift; 0 tanesi kendini başlatan akış)", markdown, StringComparison.Ordinal);
        Assert.Contains("| incident.statuscode | 2 | 1 | 2 |", markdown, StringComparison.Ordinal);
        Assert.Contains("3 iş akışı çifti arasında", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Reprocessed_Run_Says_Who_Read_The_Data_Instead_Of_Claiming_WhoAmI_Failed()
    {
        using TemporaryOutput output = new();
        (_, string first, _) = await RunHarness.RunAsync(new FakeOrganization().Build(), output);

        (ExitCode code, _, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, reprocessRunId: Path.GetFileName(first));

        Assert.True(code == ExitCode.Success, console);
        Assert.Contains("svc-crm-read", console, StringComparison.Ordinal);
        Assert.DoesNotContain("WhoAmI did not complete", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Run_Writes_The_Footprint_Reports()
    {
        using TemporaryOutput output = new();

        (_, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json"));

        Assert.False(File.Exists(Path.Combine(runRoot, "raporlar", "veri-ayak-izi.csv")), console);
        Workbook data = Workbook.Open(Path.Combine(runRoot, "raporlar", "veri-analizi.xlsx"));
        Assert.Equal(["Nasıl okunur", "Veri ayak izi", "Tetikleme zincirleri"], data.Names);
        Workbook plan = Workbook.Open(Path.Combine(runRoot, "raporlar", "tasima-plani.xlsx"));
        Assert.Contains("paylasilan_alan", plan.Headers("Taşıma planı"));
    }

    /// <summary>Two workflows close a case, one watches the field they write, one creates a task, one watches new tasks.</summary>
    private static IReadOnlyList<WorkflowIr> Organization()
    {
        return
        [
            Workflow(Closer, "Closer", "incident", [], written: ["incident.statuscode"]),
            Workflow(Stamper, "Stamper", "incident", [], written: ["incident.statuscode", "incident.new_closedby"]),
            Workflow(Watcher, "Watcher", "incident", ["statuscode"], written: [], mode: "Gerçek zamanlı"),
            Workflow(Creator, "Creator", "incident", [], written: [], creates: "task"),
            Workflow(OnNewTask, "OnNewTask", "task", [], written: [], onCreate: true)
        ];
    }

    private static WorkflowIr Workflow(Guid id, string name, string entity, IReadOnlyList<string> triggerFields,
        IReadOnlyList<string> written, string mode = "Arka plan", string? creates = null, bool onCreate = false)
    {
        IReadOnlyList<StepNode> steps = creates is null
            ? []
            : [new StepNode("0", StepKind.CreateRecord, "Create", creates, [], [], null, [], "CreateEntity", [new StepSource(id, "0")])];
        return new WorkflowIr(
            new WorkflowIdentity(id, name, null, "İş Akışı", "Tanım", entity, mode, "Organization", "Activated", false, false, null, null, null, 1, true),
            new WorkflowTrigger(onCreate, false, triggerFields, null, null, null, "Owner", false),
            steps,
            new WorkflowDependencies([], []),
            new DataTouched([], [], [], written),
            [],
            new IrProvenance("ham/xaml/x.xaml", "abc", "2026-09-23T00:00:00Z", "test"));
    }
}
