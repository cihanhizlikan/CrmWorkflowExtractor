using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// Two things a reader of the real diagrams tripped over, both on one shape. A condition whose branch STOPS the
/// process has nothing coming back to be joined, yet a join gateway was drawn anyway — a diamond with one flow in
/// and one out, which decides nothing and has to be read before that is clear. And where the condition names no
/// field, the diamond asked about the designer's generated variable, which names nothing either, while the
/// comparison itself sat only on the arrow.
/// </summary>
public sealed class GatewaySimplificationTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000010");

    private static BpmnProcess Build(string fixture)
    {
        ParseResult result = new XamlWorkflowParser(new OptionLabels()).Parse(Id, XamlWorkflowParserTests.Fixture(fixture));
        WorkflowIdentity identity = new(Id, "CTI Telefon", null, "İş Akışı", "Tanım", "phonecall", "Arka plan",
            "Kuruluş", "Etkin", false, true, null, null, null, 1, true);
        return BpmnBuilder.Build(new WorkflowIr(identity, new WorkflowTrigger(false, false, [], null, null, null, "Çağıran Kullanıcı", true),
            result.Steps, result.Dependencies, result.DataTouched, result.Warnings, new IrProvenance("x", "y", "z", "1")));
    }

    private static int Degree(BpmnProcess process, string nodeId, bool incoming)
    {
        return process.Graph.Edges.Count(edge => (incoming ? edge.TargetId : edge.SourceId) == nodeId);
    }

    [Fact]
    public void A_Gateway_That_Decides_Nothing_Is_Not_Drawn()
    {
        BpmnProcess process = Build("condition-unnamed-then-stop.xaml");

        Assert.DoesNotContain(process.Graph.Nodes, node =>
            node.Type == FlowNodeType.ExclusiveGateway
            && Degree(process, node.Id, incoming: true) == 1
            && Degree(process, node.Id, incoming: false) == 1);
    }

    /// <summary>
    /// Removing the join must not cut the flow: the path that did not stop still reaches what came after the
    /// condition, and it is still the gateway's default.
    /// </summary>
    [Fact]
    public void The_Path_That_Carries_On_Still_Reaches_The_Next_Step()
    {
        BpmnProcess process = Build("condition-unnamed-then-stop.xaml");

        FlowNode split = process.Graph.Nodes.Single(node => node.Type == FlowNodeType.ExclusiveGateway);
        FlowNode update = process.Graph.Nodes.Single(node => node.Type == FlowNodeType.ServiceTask);
        FlowEdge onward = Assert.Single(process.Graph.Edges, edge => edge.SourceId == split.Id && edge.TargetId == update.Id);

        Assert.Equal("hayır", onward.Name);
        Assert.Equal(onward.Id, split.DefaultFlow);
    }

    /// <summary>
    /// The diamond carries the comparison when there is nothing else to name, and the arrows then answer it
    /// rather than repeating it. The formal expression on the flow keeps the condition in full either way.
    /// </summary>
    [Fact]
    public void A_Condition_That_Names_No_Field_Puts_The_Comparison_On_The_Diamond()
    {
        BpmnProcess process = Build("condition-unnamed-then-stop.xaml");

        FlowNode split = process.Graph.Nodes.Single(node => node.Type == FlowNodeType.ExclusiveGateway);
        Assert.Equal("ConditionBranchStep12_1 NotEqual ps_activitytype", split.Name);

        FlowEdge taken = process.Graph.Edges.Single(edge => edge.SourceId == split.Id && edge.Name == "evet");
        Assert.Equal("ConditionBranchStep12_1 NotEqual ps_activitytype", taken.Condition);
    }
}
