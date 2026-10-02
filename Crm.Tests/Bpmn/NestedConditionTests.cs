using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// A condition nested inside another, which is the shape the real diagrams are full of. Three things a reader
/// tripped over on it: two merge gateways back to back, where the second reads as a diamond with one arrow in and
/// one out; a diamond saying "lead.leadid?" and leaving the reader to guess what is asked about it; and an arrow
/// captioned "(hiçbir koşul sağlanmazsa)", which describes how this tool drew the picture rather than anything
/// the process does.
/// </summary>
public sealed class NestedConditionTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000012");

    private static StepNode Update(string path, string field, string value)
    {
        return new StepNode(path, StepKind.UpdateRecord, "Update " + value, "lead",
            [new FieldWrite(field, [new LiteralValue(value, null)])], [], null, [], null, [new StepSource(Id, path)]);
    }

    private static Predicate On(string attribute, string comparison, string value)
    {
        return new Predicate($"lead.{attribute} {ConditionWords.Of(comparison)}" + (value.Length == 0 ? "" : " " + value),
            "lead", attribute, comparison, value.Length == 0 ? [] : [new LiteralValue(value, null)])
        {
            Subject = "lead." + attribute
        };
    }

    private static BpmnProcess Build()
    {
        StepNode inner = new("0/0/0", StepKind.Condition, "ConditionStep6", null, [],
            [
                new Branch("Düşük", On("prioritycode", "Equal", "Düşük (2)"), [Update("0/0/0/0/0", "prioritycode", "Düşük")], []),
                new Branch("Yüksek", On("prioritycode", "Equal", "Yüksek (3)"), [Update("0/0/0/1/0", "prioritycode", "Yüksek")], [])
            ],
            null, [], null, [new StepSource(Id, "0/0/0")]);
        StepNode outer = new("0", StepKind.Condition, "ConditionStep4", null, [],
            [new Branch("Var", On("leadid", "NotNull", ""), [inner], [])], null, [], null, [new StepSource(Id, "0")]);

        WorkflowIdentity identity = new(Id, "LEAD_LIFECYCLE", null, "İş Akışı", "Tanım", "appointment",
            "Gerçek zamanlı", "Kuruluş", "Etkin", false, false, null, null, null, 1, true);
        return BpmnBuilder.Build(new WorkflowIr(identity,
            new WorkflowTrigger(true, false, [], null, null, null, "Çağıran Kullanıcı", false), [outer],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("x", "y", "z", "1")));
    }

    private static FlowNode Gateway(BpmnProcess process, string name)
    {
        return process.Graph.Nodes.Single(node => node.Type == FlowNodeType.ExclusiveGateway && node.Name == name);
    }

    private static IReadOnlyList<string> Arrows(BpmnProcess process, FlowNode gateway)
    {
        return [.. process.Graph.Edges.Where(edge => edge.SourceId == gateway.Id).Select(edge => edge.Name ?? "")];
    }

    /// <summary>
    /// Two merges back to back become one. Nothing is lost: everything arriving at the inner one was already
    /// going on to the outer one.
    /// </summary>
    [Fact]
    public void Two_Merges_In_A_Row_Become_One()
    {
        BpmnProcess process = Build();

        Assert.Single(process.Graph.Nodes, node => node.Type == FlowNodeType.ExclusiveGateway && node.Name.Length == 0);
    }

    [Fact]
    public void No_Gateway_Has_One_Arrow_In_And_One_Out()
    {
        BpmnProcess process = Build();

        Assert.DoesNotContain(process.Graph.Nodes, node => node.Type == FlowNodeType.ExclusiveGateway
            && process.Graph.Edges.Count(edge => edge.TargetId == node.Id) == 1
            && process.Graph.Edges.Count(edge => edge.SourceId == node.Id) == 1);
    }

    /// <summary>One test: the diamond asks it in full, so "is that a null check?" is answered by looking at it.</summary>
    [Fact]
    public void One_Test_Is_Asked_In_Full_And_Answered_Yes_Or_No()
    {
        BpmnProcess process = Build();

        FlowNode gateway = Gateway(process, "lead.leadid dolu");
        Assert.Equal(["evet", "hayır"], Arrows(process, gateway));
    }

    /// <summary>
    /// Several tests on one field: the diamond names the field once and each arrow carries only its own operator
    /// and value, rather than repeating the field on every arrow leaving the diamond that just named it.
    /// </summary>
    [Fact]
    public void Several_Tests_On_One_Field_Name_It_Once()
    {
        BpmnProcess process = Build();

        FlowNode gateway = Gateway(process, "lead.prioritycode?");
        Assert.Equal(["= Düşük (2)", "= Yüksek (3)", "diğer"], Arrows(process, gateway));
    }

    [Fact]
    public void No_Arrow_Describes_How_The_Diagram_Was_Built()
    {
        BpmnProcess process = Build();

        Assert.DoesNotContain(process.Graph.Edges, edge => (edge.Name ?? "").Contains("koşul sağlanmazsa", StringComparison.Ordinal));
    }
}
