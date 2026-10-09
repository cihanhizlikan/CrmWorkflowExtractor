using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// A diagram label is drawn in a box and read at a glance. On the TEST estate one step's label reached 30,730
/// characters — the whole address list of the assembly behind a custom activity, 494 of them — and the file could
/// not be opened at all. Whatever a later change decides to append, it does not get to do that.
/// </summary>
public sealed class LabelBudgetTests
{
    private static readonly Guid Id = new("11111111-1111-1111-1111-111111111111");

    private static WorkflowIr Workflow(StepNode step)
    {
        WorkflowIdentity identity = new(Id, "Poliçe İptal", null, "İş Akışı", "Tanım", "new_policy", "Arka plan",
            "Kuruluş", "Etkin", false, false, null, null, null, 1, true);
        return new WorkflowIr(identity, new WorkflowTrigger(true, false, [], null, null, null, "Sahip", false), [step],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("x", "y", "z", "1"));
    }

    private static StepNode Custom(string activityType)
    {
        return new StepNode("0", StepKind.CustomActivity, "CustomActivityStep1", null, [], [], activityType, [],
            null, [new StepSource(Id, "0")]);
    }

    [Fact]
    public void No_Label_Outgrows_Its_Box_However_Much_An_Activity_Drags_Behind_It()
    {
        string many = string.Join(" · ", Enumerable.Range(0, 500).Select(index => $"https://sunucu{index}.ornek.local/servis/v2"));
        Dictionary<string, string> addresses = new(StringComparer.Ordinal) { ["Partner.Crm.Activities.NotifyPolicyService"] = many };

        BpmnProcess process = BpmnBuilder.Build(Workflow(Custom("Partner.Crm.Activities.NotifyPolicyService, Partner.Crm")),
            null, new DiagramFacts(null, null, false, addresses));

        Assert.All(process.Graph.Nodes, node => Assert.True(node.Name.Length <= BpmnBuilder.MaxLabel,
            $"'{node.Name[..Math.Min(80, node.Name.Length)]}…' is {node.Name.Length} characters"));
    }

    /// <summary>What does not fit on the label is not lost: the documentation still carries it.</summary>
    [Fact]
    public void What_The_Label_Drops_Is_Still_In_The_Documentation()
    {
        BpmnProcess process = BpmnBuilder.Build(Workflow(Custom("Partner.Crm.Activities.NotifyPolicyService, Partner.Crm")));

        FlowNode step = process.Graph.Nodes.Single(node => node.Type == FlowNodeType.ServiceTask);
        Assert.Contains("Partner.Crm.Activities.NotifyPolicyService", step.Documentation, StringComparison.Ordinal);
    }
}
