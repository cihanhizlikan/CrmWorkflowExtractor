using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// The shape that broke the layout: a condition with TWO tests, each ending the process, and no otherwise-branch —
/// so the tool adds the arrow taken when neither held, and the block is left through the DIAMOND itself.
///
/// <para>
/// Three rows then, and a diamond centred on the whole block sits on the middle one. The flow leaving it ran
/// along that line and straight THROUGH the shape standing on it, the two long captions overlapped each other,
/// and the "diğer" caption landed on an end event. An even number of branches hid all of it, which is why every
/// fixture passed: <c>CHECK_CAMPAIGN_FIELD</c> in production has three rows.
/// </para>
/// </summary>
public sealed class SpineTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000013");

    private const string FirstTest = "(campaignactivity.activityid dolu) VE ((phonecall.ps_campaignid dolu) VE (phonecall.ps_campaignid ≠ <dynamic>))";
    private const string SecondTest = "(campaign.campaignid dolu) VE ((phonecall.ps_campaignid dolu) VE (phonecall.ps_campaignid ≠ <dynamic>))";

    private static StepNode Stop(string path)
    {
        return new StepNode(path, StepKind.StopWorkflow, "İşlemi İptal Et", null, [], [], "Canceled", [], null, [new StepSource(Id, path)]);
    }

    private static Predicate Test(string text)
    {
        return new Predicate(text, "phonecall", "ps_campaignid", "NotNull", []);
    }

    /// <summary>
    /// <paramref name="tests"/> branches, each ending the process, with no otherwise-branch. Two of them is the
    /// production shape; one and three are here so the fault cannot hide behind a parity.
    /// </summary>
    private static BpmnProcess Build(int tests)
    {
        string[] texts = [FirstTest, SecondTest, FirstTest];
        List<Branch> branches = [.. Enumerable.Range(0, tests).Select(index => new Branch(
            texts[index % texts.Length], Test(texts[index % texts.Length]),
            [Stop(string.Create(CultureInfo.InvariantCulture, $"0/{index}/0"))], []))];
        StepNode condition = new("0", StepKind.Condition, "Kampanya Aktivitesi Kontrolü", null, [], branches,
            null, [], null, [new StepSource(Id, "0")]);

        WorkflowIdentity identity = new(Id, "CHECK_CAMPAIGN_FIELD", null, "İş Akışı", "Tanım", "phonecall",
            "Gerçek zamanlı", "Kuruluş", "Etkin", false, false, null, null, null, 1, true);
        return BpmnBuilder.Build(new WorkflowIr(identity,
            new WorkflowTrigger(true, false, [], null, null, null, "Sahip", false), [condition],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("x", "y", "z", "1")));
    }

    private static XDocument Xml(int tests)
    {
        return BpmnSerializer.ToXml(Build(tests), "test");
    }

    private sealed record Box(string Of, double Left, double Top, double Right, double Bottom)
    {
        public bool Overlaps(Box other)
        {
            return Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
        }
    }

    private static Box BoxOf(XElement owner, XElement bounds)
    {
        double x = Value(bounds, "x");
        double y = Value(bounds, "y");
        return new Box(owner.Attribute("bpmnElement")?.Value ?? "?", x, y, x + Value(bounds, "width"), y + Value(bounds, "height"));
    }

    private static List<Box> Shapes(XDocument xml)
    {
        return [.. xml.Descendants(BpmnSerializer.Di + "BPMNShape")
            .Select(shape => BoxOf(shape, shape.Element(BpmnSerializer.Dc + "Bounds")!))];
    }

    private static List<Box> Labels(XDocument xml)
    {
        return [.. xml.Descendants(BpmnSerializer.Di + "BPMNLabel")
            .Select(label => BoxOf(label.Parent!, label.Element(BpmnSerializer.Dc + "Bounds")!))];
    }

    private static double Value(XElement bounds, string name)
    {
        return double.Parse(bounds.Attribute(name)!.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The fault as reported. A flow may touch the shapes at its two ends and nothing else: an arrow drawn over a
    /// box tells a reader the two are connected when they are not.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_Flow_Passes_Through_A_Shape(int tests)
    {
        XDocument xml = Xml(tests);

        foreach (XElement edge in xml.Descendants(BpmnSerializer.Di + "BPMNEdge"))
        {
            XElement flow = xml.Descendants().Single(node => node.Attribute("id")?.Value == edge.Attribute("bpmnElement")!.Value);
            string[] ends = [flow.Attribute("sourceRef")?.Value ?? "", flow.Attribute("targetRef")?.Value ?? ""];
            List<(double X, double Y)> points = [.. edge.Elements(BpmnSerializer.DdDi + "waypoint")
                .Select(point => (Value(point, "x"), Value(point, "y")))];

            foreach (Box shape in Shapes(xml).Where(shape => !ends.Contains(shape.Of, StringComparer.Ordinal)))
            {
                for (int index = 1; index < points.Count; index++)
                {
                    Assert.False(Crosses(points[index - 1], points[index], shape),
                        $"{tests} test(s): the flow {edge.Attribute("bpmnElement")!.Value} runs through '{shape.Of}'.");
                }
            }
        }
    }

    /// <summary>Whether an orthogonal segment enters a box it is not an end of. Shrunk by a pixel so that merely touching an edge does not count.</summary>
    private static bool Crosses((double X, double Y) from, (double X, double Y) to, Box shape)
    {
        return Math.Min(from.X, to.X) < shape.Right - 1 && shape.Left + 1 < Math.Max(from.X, to.X)
            && Math.Min(from.Y, to.Y) < shape.Bottom - 1 && shape.Top + 1 < Math.Max(from.Y, to.Y);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_Label_Lands_On_A_Shape(int tests)
    {
        XDocument xml = Xml(tests);
        List<Box> shapes = Shapes(xml);

        foreach (Box label in Labels(xml))
        {
            foreach (Box shape in shapes)
            {
                Assert.False(label.Overlaps(shape), $"{tests} test(s): the caption of '{label.Of}' covers the shape '{shape.Of}'.");
            }
        }
    }

    /// <summary>
    /// Two captions on one another are as unreadable as one over a shape, and nothing was watching for it. A
    /// condition is never shortened, so a caption can be a hundred and sixty pixels tall and the room for it has
    /// to be reserved rather than hoped for.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_Label_Lands_On_Another_Label(int tests)
    {
        List<Box> labels = Labels(Xml(tests));

        for (int first = 0; first < labels.Count; first++)
        {
            for (int second = first + 1; second < labels.Count; second++)
            {
                Assert.False(labels[first].Overlaps(labels[second]),
                    $"{tests} test(s): the captions of '{labels[first].Of}' and '{labels[second].Of}' overlap.");
            }
        }
    }

    /// <summary>
    /// The fix that must not be undone by this one: a chain of diamonds reads as a straight line, which is what
    /// CREATE_CAMPAIGN_RESPONSE_FOR_PHONECALL already got right. The diamond, the flow leaving it and the next
    /// shape along all sit on one height.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_Start_And_The_Diamond_Stay_On_One_Line(int tests)
    {
        List<Box> shapes = Shapes(Xml(tests));
        Box start = shapes.Single(shape => shape.Of.EndsWith("_start", StringComparison.Ordinal));
        Box gateway = shapes.Single(shape => shape.Of.EndsWith("_0_split", StringComparison.Ordinal));

        Assert.Equal((start.Top + start.Bottom) / 2, (gateway.Top + gateway.Bottom) / 2, 1);
    }
}
