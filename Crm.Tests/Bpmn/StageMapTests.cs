using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Bpmn.Stages;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Bpmn;

/// <summary>
/// The stage machine of one case subcategory, drawn from its primary stage. The scenario is the one on the
/// maintainer's screen, 2026-10-09: "CRM EVRAK KONTROL" sends a case on to Nova when the documents are in order,
/// to "EVRAK BEKLENİYOR" when they are not — which sends it BACK — and the rest is what real data will throw at it:
/// a stage that loops to itself, an outcome pointing at a stage that no longer exists, a workflow this run does not
/// have, and a last stage with no way out.
/// </summary>
public sealed class StageMapTests
{
    private static readonly Guid Process = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Control = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Nova = new("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Waiting = new("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Done = new("00000000-0000-0000-0000-0000000000a4");
    private static readonly Guid Gone = new("00000000-0000-0000-0000-0000000000a9");
    private static readonly Guid SendWorkflow = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid MissingWorkflow = new("00000000-0000-0000-0000-0000000000b2");

    private static CaseStage Stage(Guid id, string shortName, bool primary, params StageExit[] exits)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["ps_slaperiod"] = "2",
            ["ps_isworkingdays"] = "Hayır",
            ["ps_queueid"] = "Emeklilik Operasyon"
        };
        return new CaseStage(id, "DT – Maaş Değişikliği | " + shortName, shortName, Process, "DT – Maaş Değişikliği",
            primary, false, true, exits, fields);
    }

    private static StageMapResult Build()
    {
        Dictionary<Guid, CaseStage> stages = new()
        {
            [Control] = Stage(Control, "CRM EVRAK KONTROL", true,
                new StageExit(StageOutcome.Success, Nova, "TALEBİ NOVAYA GÖNDER", SendWorkflow, "TALEBI_NOVAYA_GONDER", "Talebiniz alındı."),
                new StageExit(StageOutcome.Failure, Waiting, "EVRAK BEKLENİYOR", null, null, null),
                new StageExit(StageOutcome.Cancel, null, null, MissingWorkflow, "IPTAL_BILDIR", null)),
            [Nova] = Stage(Nova, "TALEBİ NOVAYA GÖNDER", false,
                new StageExit(StageOutcome.Success, Done, "TAMAMLANDI", null, null, null),
                new StageExit(StageOutcome.Failure, Control, "CRM EVRAK KONTROL", null, null, null)),
            [Waiting] = Stage(Waiting, "EVRAK BEKLENİYOR", false,
                new StageExit(StageOutcome.Success, Control, "CRM EVRAK KONTROL", null, null, null),
                new StageExit(StageOutcome.Failure, Waiting, "EVRAK BEKLENİYOR", null, null, null),
                new StageExit(StageOutcome.Cancel, Gone, "ESKİ AŞAMA", null, null, null)),
            [Done] = Stage(Done, "TAMAMLANDI", false)
        };
        XDocument workflow = BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor("condition-update-stop.xaml", SendWorkflow)), "test");
        return StageMap.Build(stages[Control], stages,
            id => id == SendWorkflow ? workflow : null,
            new Dictionary<Guid, string> { [SendWorkflow] = "is-akisi/case/talebi-novaya-gonder" }, "test");
    }

    private sealed record Box(string Of, double Left, double Top, double Right, double Bottom)
    {
        public bool Overlaps(Box other)
        {
            return Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
        }
    }

    private static double Value(XElement element, string name)
    {
        return double.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
    }

    private static Box BoxOf(string of, XElement bounds)
    {
        return new Box(of, Value(bounds, "x"), Value(bounds, "y"), Value(bounds, "x") + Value(bounds, "width"), Value(bounds, "y") + Value(bounds, "height"));
    }

    /// <summary>The map's own drawing — not the drill-down planes, whose shapes live on pages of their own.</summary>
    private static XElement MainPlane(XDocument xml)
    {
        return xml.Root!.Elements(BpmnSerializer.Di + "BPMNDiagram").First().Element(BpmnSerializer.Di + "BPMNPlane")!;
    }

    private static List<Box> Shapes(XElement plane)
    {
        return [.. plane.Elements(BpmnSerializer.Di + "BPMNShape")
            .Select(shape => BoxOf(shape.Attribute("bpmnElement")!.Value, shape.Element(BpmnSerializer.Dc + "Bounds")!))];
    }

    private static List<Box> Labels(XElement plane)
    {
        return [.. plane.Descendants(BpmnSerializer.Di + "BPMNLabel")
            .Select(label => BoxOf(label.Parent!.Attribute("bpmnElement")!.Value, label.Element(BpmnSerializer.Dc + "Bounds")!))];
    }

    [Fact]
    public void The_Map_Is_Valid_Bpmn()
    {
        Assert.Empty(BpmnSchemaValidator.Validate(Build().Xml));
    }

    /// <summary>A loop is drawn as a flow BACK, never as a second copy of the stage it returns to.</summary>
    [Fact]
    public void Every_Reachable_Stage_Is_Drawn_Once()
    {
        StageMapResult result = Build();
        XElement process = result.Xml.Root!.Element(BpmnSerializer.Model + "process")!;

        List<string> tasks = [.. process.Elements().Where(element => element.Name.LocalName is "userTask" or "serviceTask")
            .Select(task => task.Attribute("name")!.Value)];
        Assert.Equal(["CRM EVRAK KONTROL", "TALEBİ NOVAYA GÖNDER", "EVRAK BEKLENİYOR", "TAMAMLANDI"], tasks);
        Assert.Equal([Control, Nova, Waiting, Done], result.Stages);
    }

    [Fact]
    public void Every_Id_In_The_File_Is_Unique()
    {
        List<string> ids = [.. Build().Xml.Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>()];

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The fault the workflow diagrams had, which this layout is built not to have: an arrow drawn over a box.</summary>
    [Fact]
    public void No_Flow_Passes_Through_A_Shape()
    {
        XDocument xml = Build().Xml;
        XElement plane = MainPlane(xml);
        List<Box> shapes = Shapes(plane);

        foreach (XElement edge in plane.Elements(BpmnSerializer.Di + "BPMNEdge"))
        {
            XElement flow = xml.Descendants().Single(node => node.Attribute("id")?.Value == edge.Attribute("bpmnElement")!.Value);
            string[] ends = [flow.Attribute("sourceRef")!.Value, flow.Attribute("targetRef")!.Value];
            List<(double X, double Y)> points = [.. edge.Elements(BpmnSerializer.DdDi + "waypoint").Select(point => (Value(point, "x"), Value(point, "y")))];
            foreach (Box shape in shapes.Where(shape => !ends.Contains(shape.Of, StringComparer.Ordinal)))
            {
                for (int index = 1; index < points.Count; index++)
                {
                    (double X, double Y) from = points[index - 1];
                    (double X, double Y) to = points[index];
                    bool crosses = Math.Min(from.X, to.X) < shape.Right - 1 && shape.Left + 1 < Math.Max(from.X, to.X)
                        && Math.Min(from.Y, to.Y) < shape.Bottom - 1 && shape.Top + 1 < Math.Max(from.Y, to.Y);
                    Assert.False(crosses, $"{edge.Attribute("bpmnElement")!.Value} runs through {shape.Of}.");
                }
            }
        }
    }

    [Fact]
    public void Every_Flow_Is_Drawn_In_Upright_And_Level_Runs()
    {
        foreach (XElement edge in MainPlane(Build().Xml).Elements(BpmnSerializer.Di + "BPMNEdge")
            .Where(edge => !edge.Attribute("bpmnElement")!.Value.StartsWith("note_", StringComparison.Ordinal)))
        {
            List<(double X, double Y)> points = [.. edge.Elements(BpmnSerializer.DdDi + "waypoint").Select(point => (Value(point, "x"), Value(point, "y")))];
            for (int index = 1; index < points.Count; index++)
            {
                Assert.True(points[index].X == points[index - 1].X || points[index].Y == points[index - 1].Y,
                    $"{edge.Attribute("bpmnElement")!.Value} has a slanted run.");
            }
        }
    }

    [Fact]
    public void No_Label_Lands_On_A_Shape_Or_Another_Label()
    {
        XElement plane = MainPlane(Build().Xml);
        List<Box> shapes = Shapes(plane);
        List<Box> labels = Labels(plane);

        foreach (Box label in labels)
        {
            Assert.DoesNotContain(shapes, shape => label.Overlaps(shape));
        }
        for (int first = 0; first < labels.Count; first++)
        {
            for (int second = first + 1; second < labels.Count; second++)
            {
                Assert.False(labels[first].Overlaps(labels[second]), $"The captions of {labels[first].Of} and {labels[second].Of} overlap.");
            }
        }
    }

    /// <summary>
    /// Each outcome says which it is, and each leaves the diamond from a corner of its own — success to the right,
    /// failure from below, cancellation from above — so three outcomes never share a line or a caption.
    /// </summary>
    [Fact]
    public void The_Three_Outcomes_Leave_From_Three_Corners()
    {
        XDocument xml = Build().Xml;
        XElement process = xml.Root!.Element(BpmnSerializer.Model + "process")!;
        XElement plane = MainPlane(xml);
        XElement gateway = process.Elements(BpmnSerializer.Model + "exclusiveGateway").First();
        XElement gatewayBounds = plane.Elements(BpmnSerializer.Di + "BPMNShape")
            .Single(shape => shape.Attribute("bpmnElement")!.Value == gateway.Attribute("id")!.Value).Element(BpmnSerializer.Dc + "Bounds")!;
        double centreY = Value(gatewayBounds, "y") + 25;

        Dictionary<string, double> firstY = [];
        foreach (XElement flow in process.Elements(BpmnSerializer.Model + "sequenceFlow").Where(flow => flow.Attribute("sourceRef")!.Value == gateway.Attribute("id")!.Value))
        {
            Assert.Equal(flow.Attribute("name")!.Value, flow.Element(BpmnSerializer.Model + "conditionExpression")!.Value);
            XElement edge = plane.Elements(BpmnSerializer.Di + "BPMNEdge").Single(edge => edge.Attribute("bpmnElement")!.Value == flow.Attribute("id")!.Value);
            firstY[flow.Attribute("name")!.Value] = Value(edge.Elements(BpmnSerializer.DdDi + "waypoint").First(), "y");
        }

        Assert.Equal(centreY, firstY[StageOutcome.Success]);
        Assert.True(firstY[StageOutcome.Failure] > centreY);
        Assert.True(firstY[StageOutcome.Cancel] < centreY);
    }

    /// <summary>A flow that goes back runs in a lane BENEATH the whole diagram, where it can cross nothing.</summary>
    [Fact]
    public void A_Flow_Back_Runs_Beneath_Everything()
    {
        XDocument xml = Build().Xml;
        XElement process = xml.Root!.Element(BpmnSerializer.Model + "process")!;
        XElement plane = MainPlane(xml);
        double bottom = Shapes(plane).Max(shape => shape.Bottom);
        string control = process.Elements().Single(element => element.Attribute("name")?.Value == "CRM EVRAK KONTROL").Attribute("id")!.Value;

        List<XElement> back = [.. process.Elements(BpmnSerializer.Model + "sequenceFlow").Where(flow => flow.Attribute("targetRef")!.Value == control
            && flow.Attribute("sourceRef")!.Value != "u0")];
        Assert.Equal(2, back.Count);
        foreach (XElement flow in back)
        {
            XElement edge = plane.Elements(BpmnSerializer.Di + "BPMNEdge").Single(edge => edge.Attribute("bpmnElement")!.Value == flow.Attribute("id")!.Value);
            Assert.Contains(edge.Elements(BpmnSerializer.DdDi + "waypoint"), point => Value(point, "y") > bottom);
        }
    }

    /// <summary>
    /// The workflow is IN the map, as a collapsed sub-process that carries its own drawing on a plane of its own,
    /// with every id inside prefixed apart from the map's.
    /// </summary>
    [Fact]
    public void A_Workflow_Fired_On_An_Outcome_Is_Embedded_Whole()
    {
        StageMapResult result = Build();
        XElement process = result.Xml.Root!.Element(BpmnSerializer.Model + "process")!;

        XElement sub = process.Elements(BpmnSerializer.Model + "subProcess").Single(element => element.Attribute("name")!.Value == "TALEBI_NOVAYA_GONDER");
        string id = sub.Attribute("id")!.Value;
        Assert.NotEmpty(sub.Elements(BpmnSerializer.Model + "exclusiveGateway"));
        Assert.All(sub.Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>(),
            inner => Assert.StartsWith(id + "_", inner, StringComparison.Ordinal));
        Assert.Contains("bpmn/is-akisi/case/talebi-novaya-gonder.bpmn", sub.Element(BpmnSerializer.Model + "documentation")!.Value, StringComparison.Ordinal);

        XElement shape = MainPlane(result.Xml).Elements(BpmnSerializer.Di + "BPMNShape").Single(shape => shape.Attribute("bpmnElement")!.Value == id);
        Assert.Equal("false", shape.Attribute("isExpanded")!.Value);
        XElement drill = Assert.Single(result.Xml.Root!.Elements(BpmnSerializer.Di + "BPMNDiagram").Skip(1));
        Assert.Equal(id, drill.Element(BpmnSerializer.Di + "BPMNPlane")!.Attribute("bpmnElement")!.Value);
        Assert.Equal(1, result.Workflows);
    }

    /// <summary>A workflow the run does not have is still on the map, saying so, rather than silently missing.</summary>
    [Fact]
    public void A_Workflow_This_Run_Does_Not_Have_Says_So()
    {
        StageMapResult result = Build();
        XElement sub = result.Xml.Root!.Element(BpmnSerializer.Model + "process")!.Elements(BpmnSerializer.Model + "subProcess")
            .Single(element => element.Attribute("name")!.Value == "IPTAL_BILDIR");

        Assert.Contains("bulunamadı", sub.Element(BpmnSerializer.Model + "documentation")!.Value, StringComparison.Ordinal);
        Assert.Equal(1, result.MissingWorkflows);
    }

    [Fact]
    public void An_Outcome_Pointing_At_A_Stage_That_Is_Gone_Ends_There_And_Says_Which()
    {
        XElement process = Build().Xml.Root!.Element(BpmnSerializer.Model + "process")!;

        Assert.Contains(process.Elements(BpmnSerializer.Model + "endEvent"), end => end.Attribute("name")!.Value == "Aşama bulunamadı: ESKİ AŞAMA");
    }

    /// <summary>The rules a stage carries beside its transitions are in its documentation, from the one shared table.</summary>
    [Fact]
    public void A_Stage_Carries_Its_Rules()
    {
        XElement task = Build().Xml.Root!.Element(BpmnSerializer.Model + "process")!.Elements()
            .Single(element => element.Attribute("name")?.Value == "CRM EVRAK KONTROL");
        string documentation = task.Element(BpmnSerializer.Model + "documentation")!.Value;

        Assert.Contains("SLA periyodu: 2.", documentation, StringComparison.Ordinal);
        Assert.Contains("Kuyruk: Emeklilik Operasyon.", documentation, StringComparison.Ordinal);
        Assert.Contains("olumlu: sonraki aşama TALEBİ NOVAYA GÖNDER, iş akışı TALEBI_NOVAYA_GONDER, SMS \"Talebiniz alındı.\".", documentation, StringComparison.Ordinal);
    }
}
