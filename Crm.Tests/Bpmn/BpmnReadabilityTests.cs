using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// What an analyst reads: file names, the text on a diamond, and where a viewer paints each label. These are the
/// three things the first production BPMN files got wrong.
/// </summary>
public sealed class BpmnReadabilityTests
{
    [Theory]
    [InlineData("Poliçe İptal Süreci (ADMIN)", "police-iptal-sureci-admin")]
    [InlineData("DRAFT_OPEN_PHONECALL", "draft-open-phonecall")]
    [InlineData("*draft*case*step", "draft-case-step")]
    [InlineData("İKNA ARAMALARI - EMEKLİLİK", "ikna-aramalari-emeklilik")]
    [InlineData("   ", "workflow")]
    public void A_File_Name_Is_The_Workflow_Name_Folded_To_Ascii(string name, string expected)
    {
        Assert.Equal(expected, BpmnFileNames.Slug(name));
    }

    [Fact]
    public void Workflows_Sharing_A_Name_Keep_Distinct_Files()
    {
        Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid alone = Guid.Parse("33333333-3333-3333-3333-333333333333");

        IReadOnlyDictionary<Guid, string> names = BpmnFileNames.Assign([(first, "Hasar Onay"), (second, "Hasar Onay"), (alone, "Poliçe İptal")]);

        Assert.Equal("hasar-onay-11111111", names[first]);
        Assert.Equal("hasar-onay-22222222", names[second]);
        Assert.Equal("police-iptal", names[alone]);
    }

    [Fact]
    public void A_Step_The_Author_Never_Named_Is_Described_By_What_It_Does()
    {
        WorkflowIr ir = Ir([
            Step("UpdateStep3", StepKind.UpdateRecord, "new_policy", [new FieldWrite("new_status", []), new FieldWrite("new_reason", [])]),
            Step("Poliçeyi askıya al", StepKind.UpdateRecord, "new_policy", [])
        ]);

        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(ir), "test");
        List<string> names = [.. xml.Descendants(BpmnSerializer.Model + "serviceTask").Select(task => task.Attribute("name")!.Value)];

        Assert.Equal("Kayıt güncelle: new_policy · new_status, new_reason", names[0]);
        Assert.Equal("Kayıt güncelle: Poliçeyi askıya al", names[1]);
        // The internal id is evidence, so it stays in the documentation.
        Assert.Contains(xml.Descendants(BpmnSerializer.Model + "documentation"), text => text.Value.Contains("UpdateStep3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Child_Workflow_Call_Shows_The_Name_Of_What_It_Calls()
    {
        Guid child = Guid.Parse("44444444-4444-4444-4444-444444444444");
        WorkflowIr ir = Ir([Step("StartChildStep1", StepKind.StartChildWorkflow, null, []) with { Detail = child.ToString("D") }]);

        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(ir, new Dictionary<Guid, string> { [child] = "İptal Bildirimi" }), "test");

        Assert.Equal("Alt iş akışı başlat: İptal Bildirimi", xml.Descendants(BpmnSerializer.Model + "callActivity").Single().Attribute("name")!.Value);
    }

    /// <summary>Without label bounds a viewer paints a gateway's text across the diamond itself. This is that guarantee.</summary>
    [Fact]
    public void Every_Gateway_And_Event_Label_Is_Placed_Clear_Of_Its_Own_Shape()
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor("condition-update-stop.xaml")), "test");

        List<XElement> shapes = [.. xml.Descendants(BpmnSerializer.Di + "BPMNShape")];
        List<XElement> labelled = [.. shapes.Where(shape => shape.Elements(BpmnSerializer.Di + "BPMNLabel").Any())];
        Assert.NotEmpty(labelled);
        foreach (XElement shape in labelled)
        {
            XElement bounds = shape.Elements(BpmnSerializer.Dc + "Bounds").Single();
            XElement label = shape.Element(BpmnSerializer.Di + "BPMNLabel")!.Element(BpmnSerializer.Dc + "Bounds")!;
            double shapeBottom = Value(bounds, "y") + Value(bounds, "height");
            Assert.True(Value(label, "y") >= shapeBottom, "A label overlaps its own shape.");
            Assert.True(Value(label, "width") > 0 && Value(label, "height") > 0);
        }

        // Tasks are boxes and hold their own text; giving them label bounds would move it outside the box.
        List<string> taskIds = [.. xml.Descendants().Where(element => element.Name.LocalName.EndsWith("Task", StringComparison.Ordinal))
            .Select(task => task.Attribute("id")!.Value)];
        Assert.DoesNotContain(labelled, shape => taskIds.Contains(shape.Attribute("bpmnElement")!.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// A caption belongs to its own flow and has to be found beside it. It used to be placed against the TARGET
    /// shape — centred over it, above it — which left "evet" past the arrowhead and floating over the task it
    /// pointed at, and "hayır" at the far end of a flow three hundred pixels long, beside the diamond it arrived
    /// at rather than the one it left. Measured, because "looks about right" is what let that through.
    /// </summary>
    [Theory]
    [InlineData("condition-update-stop.xaml")]
    [InlineData("condition-unnamed-then-stop.xaml")]
    [InlineData("condition-attribute-read-lookup.xaml")]
    [InlineData("business-rule.xaml")]
    public void A_Branch_Caption_Sits_On_Its_Own_Flow(string fixture)
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor(fixture)), "test");

        List<XElement> named = [.. xml.Descendants(BpmnSerializer.Model + "sequenceFlow").Where(flow => flow.Attribute("name") is not null)];
        Assert.NotEmpty(named);
        foreach (XElement flow in named)
        {
            XElement edge = xml.Descendants(BpmnSerializer.Di + "BPMNEdge").Single(candidate => candidate.Attribute("bpmnElement")!.Value == flow.Attribute("id")!.Value);
            XElement label = Assert.Single(edge.Elements(BpmnSerializer.Di + "BPMNLabel")).Element(BpmnSerializer.Dc + "Bounds")!;
            List<(double X, double Y)> waypoints = [.. edge.Elements(BpmnSerializer.DdDi + "waypoint")
                .Select(point => (Value(point, "x"), Value(point, "y")))];

            double centreX = Value(label, "x") + (Value(label, "width") / 2);
            double centreY = Value(label, "y") + (Value(label, "height") / 2);
            double away = Enumerable.Range(1, waypoints.Count - 1)
                .Min(index => ToSegment(centreX, centreY, waypoints[index - 1], waypoints[index]));

            Assert.True(away <= 30,
                $"'{flow.Attribute("name")!.Value}' in {fixture} sits {away:F0} pixels from the flow it captions.");
        }
    }

    /// <summary>How far a point is from a line between two others — the nearest point on it, not an endpoint.</summary>
    private static double ToSegment(double x, double y, (double X, double Y) from, (double X, double Y) to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double square = (dx * dx) + (dy * dy);
        double along = square <= 0 ? 0 : Math.Clamp((((x - from.X) * dx) + ((y - from.Y) * dy)) / square, 0, 1);
        double nearestX = from.X + (along * dx);
        double nearestY = from.Y + (along * dy);
        return Math.Sqrt(((x - nearestX) * (x - nearestX)) + ((y - nearestY) * (y - nearestY)));
    }

    /// <summary>The guarantee: no label text lands on top of any shape in the diagram, in any fixture.</summary>
    [Theory]
    [InlineData("condition-update-stop.xaml")]
    [InlineData("child-and-custom.xaml")]
    [InlineData("wait-timeout.xaml")]
    [InlineData("production-helpers.xaml")]
    [InlineData("business-rule.xaml")]
    [InlineData("dialog.xaml")]
    public void No_Label_Lands_On_A_Shape(string fixture)
    {
        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor(fixture)), "test");
        List<XElement> shapes = [.. xml.Descendants(BpmnSerializer.Di + "BPMNShape").Select(shape => shape.Element(BpmnSerializer.Dc + "Bounds")!)];
        List<XElement> labels = [.. xml.Descendants(BpmnSerializer.Di + "BPMNLabel").Select(label => label.Element(BpmnSerializer.Dc + "Bounds")!)];

        foreach (XElement label in labels)
        {
            foreach (XElement shape in shapes)
            {
                bool overlaps = Value(label, "x") < Value(shape, "x") + Value(shape, "width")
                    && Value(shape, "x") < Value(label, "x") + Value(label, "width")
                    && Value(label, "y") < Value(shape, "y") + Value(shape, "height")
                    && Value(shape, "y") < Value(label, "y") + Value(label, "height");
                Assert.False(overlaps, $"A label at ({Value(label, "x")}, {Value(label, "y")}) covers a shape at ({Value(shape, "x")}, {Value(shape, "y")}).");
            }
        }
    }

    private static double Value(XElement bounds, string name)
    {
        return double.Parse(bounds.Attribute(name)!.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static StepNode Step(string displayName, StepKind kind, string? entity, IReadOnlyList<FieldWrite> fields)
    {
        return new StepNode("0", kind, displayName, entity, fields, [], null, [], kind.ToString(), [new StepSource(Guid.Empty, "0")]);
    }

    private static WorkflowIr Ir(IReadOnlyList<StepNode> steps)
    {
        List<StepNode> paths = [.. steps.Select((step, index) => step with { Path = index.ToString(System.Globalization.CultureInfo.InvariantCulture) })];
        return new WorkflowIr(
            new WorkflowIdentity(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Poliçe İptal", null, "İş Akışı", "Tanım", "new_policy", "Arka plan", "Kuruluş", "Etkin", false, false, null, null, null, 1, true),
            new WorkflowTrigger(true, false, [], null, null, null, "Owner", false),
            paths,
            new WorkflowDependencies([], []),
            new DataTouched([], [], [], []),
            [],
            new IrProvenance("ham/xaml/x.xaml", "abc", "2026-09-23T00:00:00Z", "test"));
    }
}
