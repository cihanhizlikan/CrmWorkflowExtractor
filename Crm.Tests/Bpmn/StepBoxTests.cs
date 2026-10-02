using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// What a step writes is the substance of the step. The label used to name three fields and end in an ellipsis —
/// "customer, prioritycode, ps_campaignresponseresultid, …" — throwing away seven of the ten that make a
/// create-record step what it is, and an analyst rebuilding it has to go and find them somewhere else. Nothing is
/// shortened to fit any more; the box is grown to hold it.
/// </summary>
public sealed class StepBoxTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000014");

    private static readonly string[] Written =
    [
        "customer", "prioritycode", "ps_campaignresponseresultid", "ps_channel", "ps_contactid",
        "ps_originatingphonecallid", "regardingobjectid", "responsecode", "subject", "transactioncurrencyid"
    ];

    private static BpmnProcess Build()
    {
        StepNode create = new("0", StepKind.CreateRecord, "CreateStep3", "campaignresponse",
            [.. Written.Select(field => new FieldWrite(field, [new LiteralValue("x", null)]))],
            [], null, [], null, [new StepSource(Id, "0")]);
        WorkflowIdentity identity = new(Id, "CREATE_CAMPAIGN_RESPONSE", null, "İş Akışı", "Tanım", "phonecall",
            "Arka plan", "Kuruluş", "Etkin", false, true, null, null, null, 1, true);
        return BpmnBuilder.Build(new WorkflowIr(identity,
            new WorkflowTrigger(false, false, [], null, null, null, "Çağıran Kullanıcı", true), [create],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("x", "y", "z", "1")));
    }

    [Fact]
    public void Every_Field_A_Step_Writes_Is_Named_On_It()
    {
        FlowNode step = Build().Graph.Nodes.Single(node => node.Type == FlowNodeType.ServiceTask);

        Assert.All(Written, field => Assert.Contains(field, step.Name, StringComparison.Ordinal));
        Assert.DoesNotContain("…", step.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Growing the text without growing the box would only move the problem: the viewer draws it over whatever is
    /// below. The box has to be able to hold its own text at the width it was given.
    /// </summary>
    [Theory]
    [InlineData("condition-update-stop.xaml")]
    [InlineData("child-and-custom.xaml")]
    [InlineData("production-helpers.xaml")]
    [InlineData("business-rule.xaml")]
    [InlineData("dialog.xaml")]
    [InlineData("action-single-custom-activity.xaml")]
    public void A_Box_Holds_Its_Own_Text(string fixture)
    {
        BpmnProcess process = BpmnBuilder.Build(BpmnEmissionTests.IrFor(fixture));

        foreach (FlowNode node in process.Graph.Nodes.Where(node => node.Name.Length > 0
            && node.Type is not (FlowNodeType.ExclusiveGateway or FlowNodeType.EventBasedGateway
                or FlowNodeType.StartEvent or FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent
                or FlowNodeType.ConditionalCatchEvent or FlowNodeType.TimerCatchEvent)))
        {
            double perLine = Math.Floor((node.Width - 20) / 6.6);
            double needed = Math.Ceiling(node.Name.Length / perLine) * 15;
            Assert.True(needed <= node.Height - 16,
                $"'{node.Name[..Math.Min(50, node.Name.Length)]}…' needs {needed:F0} of {node.Height - 16:F0} pixels in {fixture}.");
        }
    }

    /// <summary>A step that says little keeps the ordinary box; only the ones with something to say grow.</summary>
    [Fact]
    public void A_Short_Step_Keeps_The_Ordinary_Box()
    {
        Assert.Equal((130, 70), FlowNode.Size(FlowNodeType.ServiceTask, "Durum değiştir"));
    }
}
