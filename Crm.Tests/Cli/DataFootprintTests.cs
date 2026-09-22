using System.Text;
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
        Assert.Contains("(itself)", DataFootprint.Markdown([loop]), StringComparison.Ordinal);
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

        string csv = Encoding.UTF8.GetString(DataFootprint.CascadeCsv(documents));
        Assert.Contains("Closer;field update;Watcher;incident.statuscode;Background;Real-time;no", csv, StringComparison.Ordinal);
        Assert.Contains("incident;statuscode;2;", Encoding.UTF8.GetString(DataFootprint.Csv(documents)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Run_Writes_The_Footprint_Reports()
    {
        using TemporaryOutput output = new();

        (_, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output,
            importFile: Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json"));

        Assert.True(File.Exists(Path.Combine(runRoot, "reports", "data-footprint.md")), console);
        Assert.True(File.Exists(Path.Combine(runRoot, "reports", "data-footprint.csv")));
        Assert.True(File.Exists(Path.Combine(runRoot, "reports", "data-cascades.csv")));
        Assert.Contains("shared_fields_written", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(runRoot, "reports", "migration.csv"))), StringComparison.Ordinal);
    }

    /// <summary>Two workflows close a case, one watches the field they write, one creates a task, one watches new tasks.</summary>
    private static IReadOnlyList<WorkflowIr> Organization()
    {
        return
        [
            Workflow(Closer, "Closer", "incident", [], written: ["incident.statuscode"]),
            Workflow(Stamper, "Stamper", "incident", [], written: ["incident.statuscode", "incident.new_closedby"]),
            Workflow(Watcher, "Watcher", "incident", ["statuscode"], written: [], mode: "Real-time"),
            Workflow(Creator, "Creator", "incident", [], written: [], creates: "task"),
            Workflow(OnNewTask, "OnNewTask", "task", [], written: [], onCreate: true)
        ];
    }

    private static WorkflowIr Workflow(Guid id, string name, string entity, IReadOnlyList<string> triggerFields,
        IReadOnlyList<string> written, string mode = "Background", string? creates = null, bool onCreate = false)
    {
        IReadOnlyList<StepNode> steps = creates is null
            ? []
            : [new StepNode("0", StepKind.CreateRecord, "Create", creates, [], [], null, [], "CreateEntity", [new StepSource(id, "0")])];
        return new WorkflowIr(
            new WorkflowIdentity(id, name, null, "Workflow", "Definition", entity, mode, "Organization", "Activated", false, false, null, null, null, 1, true),
            new WorkflowTrigger(onCreate, false, triggerFields, null, null, null, "Owner", false),
            steps,
            new WorkflowDependencies([], []),
            new DataTouched([], [], [], written),
            [],
            new IrProvenance("raw/xaml/x.xaml", "abc", "2026-09-23T00:00:00Z", "test"));
    }
}
