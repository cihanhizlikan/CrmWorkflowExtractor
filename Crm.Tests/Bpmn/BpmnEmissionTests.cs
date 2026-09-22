using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Tests.Ir;
using Xunit;

namespace Crm.Tests.Bpmn;

public sealed class BpmnEmissionTests
{
    private static readonly Guid Id = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100");

    public static WorkflowIr IrFor(string fixture, Guid? id = null, WorkflowTrigger? trigger = null)
    {
        Guid workflowId = id ?? Id;
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(workflowId, XamlWorkflowParserTests.Fixture(fixture));
        return new WorkflowIr(
            new WorkflowIdentity(workflowId, "Poliçe İptal", null, "İş Akışı", "Tanım", "new_policy", "Arka plan", "Kuruluş", "Etkin", false, false, null, null, null, 1, true),
            trigger ?? new WorkflowTrigger(true, false, ["new_status"], "Post-operation", "Post-operation", null, "Owner", false),
            result.Steps, result.Dependencies, result.DataTouched, result.Warnings,
            new IrProvenance("ham/xaml/x.xaml", "abc", "2026-09-15T00:00:00Z", "test"));
    }

    [Theory]
    [InlineData("condition-update-stop.xaml")]
    [InlineData("child-and-custom.xaml")]
    [InlineData("wait-timeout.xaml")]
    [InlineData("unknown-construct.xaml")]
    [InlineData("production-helpers.xaml")]
    [InlineData("business-rule.xaml")]
    [InlineData("dialog.xaml")]
    public void Every_Fixture_Produces_Bpmn_That_Validates_Against_The_Omg_Schemas(string fixture)
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor(fixture)), "test");

        IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);

        Assert.Empty(errors);
    }

    [Fact]
    public void Dialog_Pages_Are_User_Tasks_And_Business_Rule_Actions_Are_Business_Rule_Tasks()
    {
        XDocument dialog = BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("dialog.xaml")), "test");
        XDocument rule = BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("business-rule.xaml")), "test");

        Assert.Contains(dialog.Descendants(BpmnSerializer.Model + "userTask"), task => task.Attribute("name")!.Value == "Diyalog sayfası: Müşteri onayı");
        Assert.Single(dialog.Descendants(BpmnSerializer.Model + "callActivity"));
        Assert.Equal(4, rule.Descendants(BpmnSerializer.Model + "businessRuleTask").Count());
        Assert.Contains(rule.Descendants(BpmnSerializer.Model + "businessRuleTask"), task => task.Attribute("name")!.Value.StartsWith("Show/hide field", StringComparison.Ordinal));
    }

    /// <summary>The validator must be able to say no, or the previous test proves nothing.</summary>
    [Fact]
    public void A_Sequence_Flow_Without_A_Source_Is_Rejected()
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("condition-update-stop.xaml")), "test");
        xml.Descendants(BpmnSerializer.Model + "sequenceFlow").First().Attribute("sourceRef")!.Remove();

        Assert.NotEmpty(BpmnSchemaValidator.Validate(xml));
    }

    [Fact]
    public void Emitting_The_Same_Ir_Twice_Is_Byte_Identical()
    {
        byte[] first = BpmnSerializer.ToBytes(BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("condition-update-stop.xaml")), "test"));
        byte[] second = BpmnSerializer.ToBytes(BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("condition-update-stop.xaml")), "test"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_Mapping_Follows_The_Handout_Table()
    {
        XElement process = Process("condition-update-stop.xaml");

        XElement start = Assert.Single(process.Elements(BpmnSerializer.Model + "startEvent"));
        Assert.NotNull(start.Element(BpmnSerializer.Model + "conditionalEventDefinition"));
        Assert.Equal("false", process.Attribute("isExecutable")?.Value);
        Assert.Equal(2, process.Elements(BpmnSerializer.Model + "exclusiveGateway").Count());
        XElement update = Assert.Single(process.Elements(BpmnSerializer.Model + "serviceTask"));
        Assert.Equal("n_0f0e0d0c0b0a09080706050403020100_0_0_0", update.Attribute("id")?.Value);
        XElement stop = Assert.Single(process.Elements(BpmnSerializer.Model + "endEvent"), end => end.Element(BpmnSerializer.Model + "terminateEventDefinition") is not null);
        Assert.Contains("Canceled", Documentation(process, stop.Attribute("id")!.Value) + stop.Attribute("name")?.Value, StringComparison.Ordinal);
        Assert.Contains(process.Elements(BpmnSerializer.Model + "sequenceFlow"),
            flow => flow.Element(BpmnSerializer.Model + "conditionExpression")?.Value == "new_policy.new_status Equal 100000003");
    }

    [Fact]
    public void Child_Workflows_Are_Call_Activities_And_Partner_Activities_Carry_Their_Type()
    {
        XElement process = Process("child-and-custom.xaml");

        XElement call = Assert.Single(process.Elements(BpmnSerializer.Model + "callActivity"));
        Assert.Equal("wf_7a0e1f529d4b4c558e1c3b6f2a9d0c11", call.Attribute("calledElement")?.Value);
        XElement custom = Assert.Single(process.Elements(BpmnSerializer.Model + "serviceTask"));
        Assert.Equal("Partner.Crm.Activities.NotifyPolicyService, Partner.Crm.Activities",
            custom.Descendants(BpmnSerializer.Provenance + "customActivity").Single().Attribute("type")?.Value);
    }

    [Fact]
    public void A_Wait_With_A_Timeout_Uses_An_Event_Based_Gateway_With_A_Timer()
    {
        XElement process = Process("wait-timeout.xaml");

        Assert.Single(process.Elements(BpmnSerializer.Model + "eventBasedGateway"));
        Assert.Contains(process.Elements(BpmnSerializer.Model + "intermediateCatchEvent"), node => node.Element(BpmnSerializer.Model + "timerEventDefinition") is not null);
        Assert.Contains(process.Elements(BpmnSerializer.Model + "intermediateCatchEvent"), node => node.Element(BpmnSerializer.Model + "conditionalEventDefinition") is not null);
    }

    [Fact]
    public void An_Unmapped_Construct_Is_A_Visible_Task_Documented_As_Unmapped()
    {
        XElement process = Process("unknown-construct.xaml");

        XElement task = Assert.Single(process.Elements(BpmnSerializer.Model + "task"));
        Assert.StartsWith("OKUNAMADI SomethingNewer", task.Attribute("name")?.Value, StringComparison.Ordinal);
        Assert.StartsWith("OKUNAMADI:", task.Element(BpmnSerializer.Model + "documentation")?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Element_Traces_Back_To_Its_Workflow_And_Step_And_Every_Node_Has_A_Shape()
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("condition-update-stop.xaml")), "test");
        XElement process = xml.Root!.Element(BpmnSerializer.Model + "process")!;

        List<XElement> stepNodes = [.. process.Elements().Where(element => element.Name.LocalName is "serviceTask" or "endEvent" or "exclusiveGateway"
            && !element.Attribute("id")!.Value.EndsWith("_join", StringComparison.Ordinal)
            && !element.Attribute("id")!.Value.EndsWith("_end", StringComparison.Ordinal))];
        Assert.Equal(3, stepNodes.Count);
        foreach (XElement node in stepNodes)
        {
            XElement source = node.Descendants(BpmnSerializer.Provenance + "source").First();
            Assert.Equal(Id.ToString("D"), source.Attribute("workflowId")?.Value);
            Assert.Contains("Poliçe İptal", node.Element(BpmnSerializer.Model + "documentation")?.Value, StringComparison.Ordinal);
        }
        HashSet<string> shapes = [.. xml.Descendants(BpmnSerializer.Di + "BPMNShape").Select(shape => shape.Attribute("bpmnElement")!.Value)];
        HashSet<string> edges = [.. xml.Descendants(BpmnSerializer.Di + "BPMNEdge").Select(shape => shape.Attribute("bpmnElement")!.Value)];
        // Everything with an id is drawn: nodes and the header note as shapes, flows and the note's link as edges.
        Assert.All(process.Elements().Where(element => element.Name.LocalName is not ("sequenceFlow" or "association") && element.Attribute("id") is not null),
            node => Assert.Contains(node.Attribute("id")!.Value, shapes));
        Assert.All(process.Elements().Where(element => element.Name.LocalName is "sequenceFlow" or "association"),
            flow => Assert.Contains(flow.Attribute("id")!.Value, edges));
    }

    [Fact]
    public void No_Two_Shapes_Overlap()
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor("condition-update-stop.xaml")), "test");
        // Shape bounds only: a label has its own bounds inside a BPMNLabel and is checked against the shapes below.
        List<(double X, double Y, double W, double H)> boxes = [.. xml.Descendants(BpmnSerializer.Di + "BPMNShape")
            .Select(shape => shape.Element(BpmnSerializer.Dc + "Bounds")!).Select(bounds =>
            (double.Parse(bounds.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture),
             double.Parse(bounds.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture),
             double.Parse(bounds.Attribute("width")!.Value, System.Globalization.CultureInfo.InvariantCulture),
             double.Parse(bounds.Attribute("height")!.Value, System.Globalization.CultureInfo.InvariantCulture)))];

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                bool overlap = boxes[i].X < boxes[j].X + boxes[j].W && boxes[j].X < boxes[i].X + boxes[i].W
                    && boxes[i].Y < boxes[j].Y + boxes[j].H && boxes[j].Y < boxes[i].Y + boxes[i].H;
                Assert.False(overlap, $"Shapes {i} and {j} overlap.");
            }
        }
    }

    private static XElement Process(string fixture)
    {
        return BpmnSerializer.ToXml(BpmnBuilder.Build(IrFor(fixture)), "test").Root!.Element(BpmnSerializer.Model + "process")!;
    }

    private static string Documentation(XElement process, string id)
    {
        return process.Elements().Single(element => element.Attribute("id")?.Value == id).Element(BpmnSerializer.Model + "documentation")?.Value ?? "";
    }
}
