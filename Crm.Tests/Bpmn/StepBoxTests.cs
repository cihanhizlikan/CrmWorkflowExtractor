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

    private static FlowNode Writes(params FieldWrite[] fields)
    {
        StepNode update = new("0", StepKind.UpdateRecord, "UpdateStep1", "phonecall", fields, [], null, [], null, [new StepSource(Id, "0")]);
        WorkflowIdentity identity = new(Id, "CLOSE_CTI_CALLS", null, "İş Akışı", "Tanım", "phonecall",
            "Arka plan", "Kuruluş", "Etkin", false, true, null, null, null, 1, true);
        BpmnProcess process = BpmnBuilder.Build(new WorkflowIr(identity,
            new WorkflowTrigger(false, false, [], null, null, null, "Çağıran Kullanıcı", true), [update],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("x", "y", "z", "1")));
        return process.Graph.Nodes.Single(node => node.Type == FlowNodeType.ServiceTask);
    }

    /// <summary>
    /// The value a field is SET TO, where the definition fixes it. Naming the fields alone left the substance of
    /// the step — what it actually does to them — readable only in the documentation (maintainer, 2026-10-08).
    /// </summary>
    [Fact]
    public void A_Value_The_Definition_Fixes_Is_On_The_Box()
    {
        FlowNode step = Writes(
            new FieldWrite("description", [new LiteralValue("VD-92418 numaralı talebe istinaden kapatılmıştır.", null)]),
            new FieldWrite("ps_activityresultcode", [new LiteralValue("99", null)]),
            new FieldWrite("ps_activitysubresultid", [new LiteralValue("3", "Aramadan İşlem Yapılmıştır")]));

        Assert.Contains("description = VD-92418 numaralı talebe istinaden kapatılmıştır.", step.Name, StringComparison.Ordinal);
        Assert.Contains("ps_activityresultcode = 99", step.Name, StringComparison.Ordinal);
        // An option keeps its number behind its label: the label is for the reader, the number is what CRM holds.
        Assert.Contains("ps_activitysubresultid = Aramadan İşlem Yapılmıştır (3)", step.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value CRM works out as it runs is not one the definition fixes, and "= &lt;dynamic&gt;" costs a line of
    /// the box to tell a reader nothing. Those fields are named alone, beside the ones that do say something.
    /// </summary>
    [Fact]
    public void A_Value_Only_Known_At_Run_Time_Is_Left_Off()
    {
        FlowNode step = Writes(
            new FieldWrite("customer", [new LiteralValue(LiteralValue.Dynamic, null)]),
            new FieldWrite("prioritycode", [new LiteralValue("1", "Normal")]),
            new FieldWrite("regardingobjectid", []));

        Assert.DoesNotContain(LiteralValue.Dynamic, step.Name, StringComparison.Ordinal);
        Assert.Contains("customer,", step.Name, StringComparison.Ordinal);
        Assert.Contains("prioritycode = Normal (1)", step.Name, StringComparison.Ordinal);
        Assert.EndsWith("regardingobjectid", step.Name, StringComparison.Ordinal);
    }

    /// <summary>Half a list is worse than none: a field is shown with its values only when ALL of them are fixed.</summary>
    [Fact]
    public void A_Field_With_One_Value_Left_To_Run_Time_Is_Named_Alone()
    {
        FlowNode step = Writes(new FieldWrite("statuscode", [new LiteralValue("1", "Açık"), new LiteralValue(LiteralValue.Dynamic, null)]));

        Assert.EndsWith("statuscode", step.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("Açık", step.Name, StringComparison.Ordinal);
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
