using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// The diamond asks a question, and it used to ask "Koşul" — the word "condition", which asks nothing. The full
/// condition was on the outgoing arrow and nowhere else, so a reader looking at the shape that decides the flow
/// could not see what it decided on.
/// </summary>
public sealed class GatewayQuestionTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000001");

    private static BpmnProcess Build(string fixture, OptionLabels labels)
    {
        ParseResult result = new XamlWorkflowParser(labels).Parse(Id, XamlWorkflowParserTests.Fixture(fixture));
        WorkflowIdentity identity = new(Id, "Poliçe İptal", null, "İş Akışı", "Tanım", "new_policy", "Arka plan",
            "Kuruluş", "Etkin", false, false, null, null, null, 1, true);
        return BpmnBuilder.Build(new WorkflowIr(identity, new WorkflowTrigger(true, false, [], null, null, null, "Sahip", false),
            result.Steps, result.Dependencies, result.DataTouched, result.Warnings, new IrProvenance("x", "y", "z", "1")));
    }

    private static FlowNode Gateway(BpmnProcess process)
    {
        return process.Graph.Nodes.First(node => node.Type == FlowNodeType.ExclusiveGateway && node.Name.Length > 0);
    }

    [Fact]
    public void A_Gateway_On_A_Field_Asks_About_That_Field()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");

        Assert.Equal("new_policy.new_status?", Gateway(Build("condition-update-stop.xaml", labels)).Name);
    }

    /// <summary>
    /// When the condition compares what an activity returned there is no field to name, and the diamond used to
    /// fall back to the bare word. It now carries whatever the condition is about — a designer's own variable is
    /// a poor name, but it is a name, and it can be searched for.
    /// </summary>
    [Fact]
    public void A_Gateway_On_An_Activity_Output_Asks_About_That_Output()
    {
        FlowNode gateway = Gateway(Build("condition-on-activity-output.xaml", new OptionLabels()));

        Assert.Equal("CheckPolicyStatus.Durum?", gateway.Name);
        Assert.NotEqual("Koşul", gateway.Name);
    }

    /// <summary>The diamond has room for a question; its documentation has room for every branch's full condition.</summary>
    [Fact]
    public void The_Gateway_Documentation_Carries_Every_Branch_Condition_In_Full()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");

        FlowNode gateway = Gateway(Build("condition-update-stop.xaml", labels));

        Assert.Contains("new_policy.new_status Equal İptal Edildi (100000003)", gateway.Documentation, StringComparison.Ordinal);
    }
}
