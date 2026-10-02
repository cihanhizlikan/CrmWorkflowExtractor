using Crm.Bpmn;
using Crm.Bpmn.Graph;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// The single-step diagrams, of which the estate has many. One is an Action whose whole body is a call into
/// compiled code, and its label carried the assembly-qualified type — where the code LIVES — truncated mid-word,
/// which is the one thing about it a reader cannot act on. What the definition does hold is the names it is
/// called with and the names it writes back, and for an Action, the Action's own parameters with their types.
/// That is the contract, and it is all that exists outside the compiled code.
/// </summary>
public sealed class ActivitySignatureTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000013");

    private static BpmnProcess Build()
    {
        ParseResult result = new XamlWorkflowParser(new OptionLabels())
            .Parse(Id, XamlWorkflowParserTests.Fixture("action-single-custom-activity.xaml"));
        WorkflowIdentity identity = new(Id, "CheckRetirementEligibility", null, "Eylem", "Tanım", null, "Arka plan",
            "Kuruluş", "Etkin", false, true, null, null, null, 1, true);
        return BpmnBuilder.Build(new WorkflowIr(identity,
            new WorkflowTrigger(false, false, [], null, null, null, "Çağıran Kullanıcı", true), result.Steps,
            result.Dependencies, result.DataTouched, result.Warnings, new IrProvenance("x", "y", "z", "1"))
        {
            Parameters = result.Parameters
        });
    }

    [Fact]
    public void The_Label_Is_The_Signature_Not_Where_The_Code_Lives()
    {
        FlowNode step = Build().Graph.Nodes.Single(node => node.Type == FlowNodeType.ServiceTask);

        Assert.Equal("Özel etkinlik: CheckRetirementEligibility(ContactId) → CanProceed, WarningMessage", step.Name);
        Assert.DoesNotContain("PublicKeyToken", step.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which way an argument goes is stated by the element that carries it, so the documentation can say it —
    /// and must not where the XAML does not. An argument written as a plain attribute carries no direction, and
    /// CRM does write back through one elsewhere, so it keeps the neutral wording rather than a guess.
    /// </summary>
    [Fact]
    public void Each_Argument_Says_Which_Way_It_Goes_Where_The_Xaml_Says_So()
    {
        FlowNode step = Build().Graph.Nodes.Single(node => node.Type == FlowNodeType.ServiceTask);

        Assert.Contains("Çıktı: CanProceed", step.Documentation, StringComparison.Ordinal);
        Assert.Contains("Çıktı: WarningMessage", step.Documentation, StringComparison.Ordinal);
        Assert.Contains("Bağımsız değişken: ContactId", step.Documentation, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Action's own parameters, with CRM's plumbing left out — InputEntities and CreatedEntities are on every
    /// definition in the estate and say nothing about any of them.
    /// </summary>
    [Fact]
    public void The_Note_Carries_What_The_Action_Takes_And_Returns()
    {
        BpmnProcess process = Build();

        Assert.Contains("Parametreler: girdi ContactId (String) · çıktı CanProceed (Boolean), WarningMessage (String)",
            process.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("InputEntities", process.Note, StringComparison.Ordinal);
    }

    /// <summary>A plain workflow declares only plumbing, so it gains no parameter line at all.</summary>
    [Fact]
    public void A_Workflow_With_Only_Plumbing_Gains_No_Parameter_Line()
    {
        ParseResult result = new XamlWorkflowParser(new OptionLabels())
            .Parse(Id, XamlWorkflowParserTests.Fixture("condition-update-stop.xaml"));

        Assert.Empty(result.Parameters);
    }
}
