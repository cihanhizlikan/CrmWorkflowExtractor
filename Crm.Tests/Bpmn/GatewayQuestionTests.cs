using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// What a decision says, and where it says it. A diamond reading <c>lead.leadid?</c> names the field and leaves
/// the reader asking what is being asked ABOUT it — a null check, a match, a range? — while the answer sat on the
/// arrow, where it read as a repetition of the diamond. So the diamond carries the comparison where there is one
/// test, and the arrows become yes and no. Where there are several tests the diamond names the field they share
/// and each arrow carries its own operator and value.
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

    private static IReadOnlyList<string> Arrows(BpmnProcess process, FlowNode gateway)
    {
        return [.. process.Graph.Edges.Where(edge => edge.SourceId == gateway.Id).Select(edge => edge.Name ?? "")];
    }

    /// <summary>One test, so the diamond asks it in full and the arrows answer it.</summary>
    [Fact]
    public void One_Test_Is_Asked_In_Full_On_The_Diamond()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");

        BpmnProcess process = Build("condition-update-stop.xaml", labels);
        FlowNode gateway = Gateway(process);

        Assert.Equal("new_policy.new_status = İptal Edildi (100000003)", gateway.Name);
        Assert.Equal(["evet", "hayır"], Arrows(process, gateway));
    }

    [Fact]
    public void One_Test_On_An_Activity_Output_Is_Asked_The_Same_Way()
    {
        BpmnProcess process = Build("condition-on-activity-output.xaml", new OptionLabels());
        FlowNode gateway = Gateway(process);

        Assert.Equal("CheckPolicyStatus.Durum = Aktif", gateway.Name);
        Assert.Contains("evet", Arrows(process, gateway));
    }

    /// <summary>
    /// The arrow taken when nothing held is an OUTCOME. It used to read "(hiçbir koşul sağlanmazsa)", which
    /// described how this tool built the diagram rather than anything about the process.
    /// </summary>
    [Fact]
    public void The_Arrow_Taken_When_Nothing_Held_Says_So_In_One_Word()
    {
        BpmnProcess process = Build("condition-unnamed-then-stop.xaml", new OptionLabels());

        Assert.DoesNotContain(process.Graph.Edges, edge => (edge.Name ?? "").Contains("koşul sağlanmazsa", StringComparison.Ordinal));
        Assert.Contains(process.Graph.Edges, edge => edge.Name == "hayır");
    }

    /// <summary>The gateway's documentation still carries every branch's condition in full, however short the labels.</summary>
    [Fact]
    public void The_Gateway_Documentation_Carries_Every_Branch_Condition_In_Full()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");

        FlowNode gateway = Gateway(Build("condition-update-stop.xaml", labels));

        Assert.Contains("new_policy.new_status = İptal Edildi (100000003)", gateway.Documentation, StringComparison.Ordinal);
    }
}
