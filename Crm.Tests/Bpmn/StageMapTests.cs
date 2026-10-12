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

    private static readonly XNamespace Model = BpmnSerializer.Model;
    private static readonly XNamespace Di = BpmnSerializer.Di;

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

    private static XDocument Diagram(Guid id)
    {
        return BpmnSerializer.ToXml(BpmnBuilder.Build(BpmnEmissionTests.IrFor("condition-update-stop.xaml", id)), "test");
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
        XDocument workflow = Diagram(SendWorkflow);
        return StageMap.Build(stages[Control], stages,
            id => id == SendWorkflow ? workflow : null,
            new Dictionary<Guid, string> { [SendWorkflow] = "bpmn/is-akislari/is-akisi/case/talebi-novaya-gonder" }, "test");
    }

    private static Guid Step(int number)
    {
        return new Guid($"00000000-0000-0000-0000-0000000001{number:D2}");
    }

    private static Guid Flow(int number)
    {
        return new Guid($"00000000-0000-0000-0000-0000000002{number:D2}");
    }

    private static CaseStage Numbered(int number, bool primary, params StageExit[] exits)
    {
        string name = number switch
        {
            2 or 8 => "BAŞKA EKİBE Mİ?",
            3 or 9 => "İŞLE",
            _ => "AŞAMA " + number.ToString(CultureInfo.InvariantCulture)
        };
        return new CaseStage(Step(number), "Reklam | " + name, name, Process, "Reklam", primary, false, true, exits,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// The shape of the "Reklam" map the maintainer sent on 2026-10-12, with every name invented: eleven stages, three of
    /// whose outcomes all agree, one workflow fired from fourteen exits, two pairs of stages sharing a name, loops back
    /// to the second stage from far to its right, and an outcome pointing at a stage that is gone.
    /// </summary>
    private static StageMapResult BuildCampaign()
    {
        List<CaseStage> list =
        [
            Numbered(1, true,
                new StageExit(StageOutcome.Success, Step(2), "A2", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Failure, Step(2), "A2", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Cancel, Step(2), "A2", Flow(1), "WF_1", null)),
            Numbered(2, false,
                new StageExit(StageOutcome.Success, Step(3), "A3", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Failure, Step(4), "A4", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Cancel, null, null, Flow(1), "WF_1", null)),
            Numbered(3, false,
                new StageExit(StageOutcome.Success, Step(2), "A2", Flow(2), "WF_2", "Talebiniz işleme alındı."),
                new StageExit(StageOutcome.Failure, Step(4), "A4", null, null, null)),
            Numbered(4, false,
                new StageExit(StageOutcome.Success, Step(5), "A5", Flow(3), "WF_3", null),
                new StageExit(StageOutcome.Failure, Step(6), "A6", Flow(2), "WF_2", null),
                new StageExit(StageOutcome.Cancel, Step(2), "A2", null, null, null)),
            Numbered(5, false,
                new StageExit(StageOutcome.Success, Step(6), "A6", Flow(4), "WF_4", null),
                new StageExit(StageOutcome.Failure, null, null, Flow(5), "WF_5", null),
                new StageExit(StageOutcome.Cancel, Step(6), "A6", Flow(4), "WF_4", null)),
            Numbered(6, false,
                new StageExit(StageOutcome.Success, Step(7), "A7", null, null, null),
                new StageExit(StageOutcome.Failure, Step(8), "A8", null, null, null),
                new StageExit(StageOutcome.Cancel, Step(8), "A8", null, null, null)),
            Numbered(7, false,
                new StageExit(StageOutcome.Success, Step(8), "A8", null, null, null),
                new StageExit(StageOutcome.Failure, Step(8), "A8", null, null, null),
                new StageExit(StageOutcome.Cancel, Step(8), "A8", null, null, null)),
            Numbered(8, false,
                new StageExit(StageOutcome.Success, Step(9), "A9", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Failure, Step(10), "A10", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Cancel, null, null, Flow(1), "WF_1", null)),
            Numbered(9, false,
                new StageExit(StageOutcome.Success, Step(8), "A8", Flow(2), "WF_2", null),
                new StageExit(StageOutcome.Failure, Step(4), "A4", null, null, null)),
            Numbered(10, false,
                new StageExit(StageOutcome.Success, null, null, Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Failure, Step(8), "A8", Flow(2), "WF_2", null),
                new StageExit(StageOutcome.Cancel, Step(11), "A11", Flow(1), "WF_1", null)),
            Numbered(11, false,
                new StageExit(StageOutcome.Success, Step(2), "A2", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Failure, Step(2), "A2", Flow(1), "WF_1", null),
                new StageExit(StageOutcome.Cancel, Gone, "YOK", Flow(1), "WF_1", null))
        ];
        Dictionary<Guid, CaseStage> stages = list.ToDictionary(stage => stage.Id);
        Dictionary<Guid, XDocument> diagrams = [];
        return StageMap.Build(list[0], stages, id => diagrams.TryGetValue(id, out XDocument? found) ? found : diagrams[id] = Diagram(id),
            new Dictionary<Guid, string>(), "test");
    }

    public static TheoryData<string> Maps()
    {
        return ["evrak", "kampanya"];
    }

    private static StageMapResult Map(string name)
    {
        return name == "evrak" ? Build() : BuildCampaign();
    }

    private sealed record Box(string Of, double Left, double Top, double Right, double Bottom)
    {
        public bool Overlaps(Box other)
        {
            return Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
        }
    }

    private sealed record Segment(string Of, (double X, double Y) From, (double X, double Y) To)
    {
        public bool Level
        {
            get { return From.Y == To.Y; }
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
        return xml.Root!.Elements(Di + "BPMNDiagram").First().Element(Di + "BPMNPlane")!;
    }

    private static XElement ProcessOf(XDocument xml)
    {
        return xml.Root!.Element(Model + "process")!;
    }

    private static List<Box> Shapes(XElement plane)
    {
        return [.. plane.Elements(Di + "BPMNShape")
            .Select(shape => BoxOf(shape.Attribute("bpmnElement")!.Value, shape.Element(BpmnSerializer.Dc + "Bounds")!))];
    }

    private static List<Box> Labels(XElement plane)
    {
        return [.. plane.Descendants(Di + "BPMNLabel")
            .Select(label => BoxOf(label.Parent!.Attribute("bpmnElement")!.Value, label.Element(BpmnSerializer.Dc + "Bounds")!))];
    }

    private static List<(double X, double Y)> Points(XElement edge)
    {
        return [.. edge.Elements(BpmnSerializer.DdDi + "waypoint").Select(point => (Value(point, "x"), Value(point, "y")))];
    }

    private static List<Segment> Segments(XElement plane)
    {
        List<Segment> segments = [];
        foreach (XElement edge in plane.Elements(Di + "BPMNEdge").Where(edge => !edge.Attribute("bpmnElement")!.Value.StartsWith("note_", StringComparison.Ordinal)))
        {
            List<(double X, double Y)> points = Points(edge);
            for (int index = 1; index < points.Count; index++)
            {
                segments.Add(new Segment(edge.Attribute("bpmnElement")!.Value, points[index - 1], points[index]));
            }
        }
        return segments;
    }

    private static bool Inside(double value, double one, double other)
    {
        return value > Math.Min(one, other) && value < Math.Max(one, other);
    }

    /// <summary>Pairs of flows whose runs cross: a level run of one passing strictly through an upright run of the other.</summary>
    private static List<(string, string)> Crossings(XElement plane)
    {
        List<Segment> segments = Segments(plane);
        List<(string, string)> crossings = [];
        foreach (Segment level in segments.Where(segment => segment.Level))
        {
            foreach (Segment upright in segments.Where(segment => !segment.Level && segment.Of != level.Of))
            {
                if (Inside(upright.From.X, level.From.X, level.To.X) && Inside(level.From.Y, upright.From.Y, upright.To.Y))
                {
                    crossings.Add((level.Of, upright.Of));
                }
            }
        }
        return crossings;
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void The_Map_Is_Valid_Bpmn(string map)
    {
        Assert.Empty(BpmnSchemaValidator.Validate(Map(map).Xml));
    }

    /// <summary>A loop is drawn as a flow BACK, never as a second copy of the stage it returns to.</summary>
    [Fact]
    public void Every_Reachable_Stage_Is_Drawn_Once()
    {
        StageMapResult result = Build();

        List<string> tasks = [.. ProcessOf(result.Xml).Elements().Where(element => element.Name.LocalName is "userTask" or "serviceTask")
            .Select(task => task.Attribute("name")!.Value)];
        Assert.Equal(["CRM EVRAK KONTROL", "EVRAK BEKLENİYOR", "TALEBİ NOVAYA GÖNDER", "TAMAMLANDI"], tasks.Order(StringComparer.Ordinal));
        Assert.Equal([Control, Nova, Waiting, Done], result.Stages.Select(stage => stage.Id));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Every_Id_In_The_File_Is_Unique(string map)
    {
        List<string> ids = [.. Map(map).Xml.Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>()];

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A stage's ids come from CRM's own, so the same map from two runs compares element for element.</summary>
    [Fact]
    public void A_Stage_Is_Named_By_Its_Crm_Id()
    {
        XElement process = ProcessOf(Build().Xml);

        Assert.Contains(process.Elements(), element => element.Attribute("id")?.Value == "asama_" + Control.ToString("N"));
        Assert.Contains(process.Elements(), element => element.Attribute("id")?.Value == "asama_" + Control.ToString("N") + "_karar");
    }

    /// <summary>The fault the workflow diagrams had, which this layout is built not to have: an arrow drawn over a box.</summary>
    [Theory]
    [MemberData(nameof(Maps))]
    public void No_Flow_Passes_Through_A_Shape(string map)
    {
        XDocument xml = Map(map).Xml;
        XElement plane = MainPlane(xml);
        List<Box> shapes = Shapes(plane);

        foreach (XElement edge in plane.Elements(Di + "BPMNEdge"))
        {
            XElement flow = xml.Descendants().Single(node => node.Attribute("id")?.Value == edge.Attribute("bpmnElement")!.Value);
            string[] ends = [flow.Attribute("sourceRef")!.Value, flow.Attribute("targetRef")!.Value];
            List<(double X, double Y)> points = Points(edge);
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

    [Theory]
    [MemberData(nameof(Maps))]
    public void Every_Flow_Is_Drawn_In_Upright_And_Level_Runs(string map)
    {
        foreach (Segment segment in Segments(MainPlane(Map(map).Xml)))
        {
            Assert.True(segment.From.X == segment.To.X || segment.From.Y == segment.To.Y, $"{segment.Of} has a slanted run.");
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void No_Label_Lands_On_A_Shape_Or_Another_Label(string map)
    {
        XElement plane = MainPlane(Map(map).Xml);
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
    /// Two flows never share a line: flows into one shape come in at heights of their own, and upright runs stand in
    /// slots of their own. On the Reklam map of 2026-10-12, 57 of 58 such overlaps were flows sharing their target's middle.
    /// </summary>
    [Theory]
    [MemberData(nameof(Maps))]
    public void No_Two_Flows_Share_A_Line(string map)
    {
        List<Segment> segments = Segments(MainPlane(Map(map).Xml));
        for (int first = 0; first < segments.Count; first++)
        {
            for (int second = first + 1; second < segments.Count; second++)
            {
                Segment one = segments[first];
                Segment other = segments[second];
                if (one.Of == other.Of || one.Level != other.Level)
                {
                    continue;
                }
                bool sameLine = one.Level ? one.From.Y == other.From.Y : one.From.X == other.From.X;
                double low = one.Level ? Math.Max(Math.Min(one.From.X, one.To.X), Math.Min(other.From.X, other.To.X)) : Math.Max(Math.Min(one.From.Y, one.To.Y), Math.Min(other.From.Y, other.To.Y));
                double high = one.Level ? Math.Min(Math.Max(one.From.X, one.To.X), Math.Max(other.From.X, other.To.X)) : Math.Min(Math.Max(one.From.Y, one.To.Y), Math.Max(other.From.Y, other.To.Y));
                Assert.False(sameLine && high - low > 1, $"{one.Of} and {other.Of} run along one line.");
            }
        }
    }

    /// <summary>
    /// The measured map had 52 crossings, most of them flows going back through lanes beneath it. With flows back made
    /// link events, the upright runs ordered and flows into a shape spread, the same shape crosses at most once.
    /// </summary>
    [Fact]
    public void The_Campaign_Map_Barely_Crosses_Itself()
    {
        List<(string, string)> crossings = Crossings(MainPlane(BuildCampaign().Xml));

        Assert.True(crossings.Count <= 1, string.Join("; ", crossings));
    }

    /// <summary>The flows out of one diamond never cross one another: each leaves from its own corner and the slots follow.</summary>
    [Theory]
    [MemberData(nameof(Maps))]
    public void The_Flows_Out_Of_One_Diamond_Never_Cross(string map)
    {
        XDocument xml = Map(map).Xml;
        Dictionary<string, string> source = ProcessOf(xml).Elements(Model + "sequenceFlow")
            .ToDictionary(flow => flow.Attribute("id")!.Value, flow => flow.Attribute("sourceRef")!.Value);

        Assert.DoesNotContain(Crossings(MainPlane(xml)), pair => source[pair.Item1] == source[pair.Item2]);
    }

    /// <summary>
    /// Each outcome says which it is, and each leaves the diamond from a corner of its own — success to the right,
    /// failure from below, cancellation from above — so three outcomes never share a line or a caption.
    /// </summary>
    [Fact]
    public void The_Three_Outcomes_Leave_From_Three_Corners()
    {
        XDocument xml = Build().Xml;
        XElement process = ProcessOf(xml);
        XElement plane = MainPlane(xml);
        string gateway = "asama_" + Control.ToString("N") + "_karar";
        XElement gatewayBounds = plane.Elements(Di + "BPMNShape")
            .Single(shape => shape.Attribute("bpmnElement")!.Value == gateway).Element(BpmnSerializer.Dc + "Bounds")!;
        double centreY = Value(gatewayBounds, "y") + 25;

        Dictionary<string, double> firstY = [];
        foreach (XElement flow in process.Elements(Model + "sequenceFlow").Where(flow => flow.Attribute("sourceRef")!.Value == gateway))
        {
            Assert.Equal(flow.Attribute("name")!.Value, flow.Element(Model + "conditionExpression")!.Value);
            XElement edge = plane.Elements(Di + "BPMNEdge").Single(edge => edge.Attribute("bpmnElement")!.Value == flow.Attribute("id")!.Value);
            firstY[flow.Attribute("name")!.Value] = Points(edge)[0].Y;
        }

        Assert.Equal(centreY, firstY[StageOutcome.Success]);
        Assert.True(firstY[StageOutcome.Failure] > centreY);
        Assert.True(firstY[StageOutcome.Cancel] < centreY);
    }

    /// <summary>
    /// Outcomes that lead to the same place through the same workflow are one flow, named for all of them, and a stage
    /// with nothing to decide has no diamond: one that branches three ways to one place reads as a modelling error.
    /// </summary>
    [Fact]
    public void Outcomes_That_Agree_Are_One_Flow()
    {
        XElement process = ProcessOf(BuildCampaign().Xml);
        string first = "asama_" + Step(1).ToString("N");

        Assert.DoesNotContain(process.Elements(Model + "exclusiveGateway"), gateway => gateway.Attribute("id")!.Value == first + "_karar");
        XElement flow = Assert.Single(process.Elements(Model + "sequenceFlow"), flow => flow.Attribute("sourceRef")!.Value == first);
        Assert.Equal("olumlu / olumsuz / iptal", flow.Attribute("name")!.Value);
        Assert.Null(flow.Element(Model + "conditionExpression"));
    }

    /// <summary>
    /// A workflow fired whichever way the stage closes is drawn ONCE, between the stage and its diamond — not once on
    /// each branch.
    /// </summary>
    [Fact]
    public void A_Workflow_Fired_On_Every_Outcome_Stands_Before_The_Diamond()
    {
        XElement process = ProcessOf(BuildCampaign().Xml);
        string second = "asama_" + Step(2).ToString("N");
        string box = second + "_wf_" + Flow(1).ToString("N");

        Assert.Contains(process.Elements(Model + "sequenceFlow"), flow => flow.Attribute("sourceRef")!.Value == second && flow.Attribute("targetRef")!.Value == box);
        Assert.Contains(process.Elements(Model + "sequenceFlow"), flow => flow.Attribute("sourceRef")!.Value == box && flow.Attribute("targetRef")!.Value == second + "_karar");
        Assert.DoesNotContain(process.Elements(), element => element.Attribute("id")?.Value.StartsWith(second + "_olumlu_wf", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// The workflow is IN the map, as a collapsed sub-process that carries its own drawing on a plane of its own,
    /// with every id inside prefixed apart from the map's.
    /// </summary>
    [Fact]
    public void A_Workflow_Fired_On_An_Outcome_Is_Embedded_Whole()
    {
        StageMapResult result = Build();
        XElement process = ProcessOf(result.Xml);

        XElement sub = process.Elements(Model + "subProcess").Single(element => element.Attribute("name")!.Value == "TALEBI_NOVAYA_GONDER");
        string id = sub.Attribute("id")!.Value;
        Assert.NotEmpty(sub.Elements(Model + "exclusiveGateway"));
        Assert.All(sub.Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>(),
            inner => Assert.StartsWith(id + "_", inner, StringComparison.Ordinal));
        string documentation = sub.Element(Model + "documentation")!.Value;
        Assert.Contains("bpmn/is-akislari/is-akisi/case/talebi-novaya-gonder.bpmn", documentation, StringComparison.Ordinal);
        Assert.Contains("Çalıştıran: CRM EVRAK KONTROL — olumlu", documentation, StringComparison.Ordinal);

        XElement shape = MainPlane(result.Xml).Elements(Di + "BPMNShape").Single(shape => shape.Attribute("bpmnElement")!.Value == id);
        Assert.Equal("false", shape.Attribute("isExpanded")!.Value);
        XElement drill = Assert.Single(result.Xml.Root!.Elements(Di + "BPMNDiagram").Skip(1));
        Assert.Equal(id, drill.Element(Di + "BPMNPlane")!.Attribute("bpmnElement")!.Value);
        Assert.Equal(2, result.Workflows);
    }

    /// <summary>
    /// A workflow fired from many places is embedded ONCE per map — in the first box a reader meets — and every other
    /// box for it is a call activity saying where that one is. Fourteen copies of one workflow made most of a 2.6 MB map.
    /// </summary>
    [Fact]
    public void A_Workflow_Fired_From_Many_Places_Is_Embedded_Once()
    {
        StageMapResult result = BuildCampaign();
        XElement process = ProcessOf(result.Xml);

        foreach (IGrouping<string, XElement> workflow in process.Elements().Where(element => element.Name.LocalName is "subProcess" or "callActivity")
            .GroupBy(element => element.Attribute("name")!.Value, StringComparer.Ordinal))
        {
            XElement embedded = Assert.Single(workflow, box => box.Name.LocalName == "subProcess");
            Assert.All(workflow.Where(box => box != embedded), call =>
                Assert.Contains("ilk geçtiği kutunun içinde", call.Element(Model + "documentation")!.Value, StringComparison.Ordinal));

            XElement plane = MainPlane(result.Xml);
            double Left(XElement box)
            {
                return Value(plane.Elements(Di + "BPMNShape").Single(shape => shape.Attribute("bpmnElement")!.Value == box.Attribute("id")!.Value)
                    .Element(BpmnSerializer.Dc + "Bounds")!, "x");
            }
            Assert.Equal(workflow.Min(Left), Left(embedded));
        }
        Assert.Equal(5, result.Workflows);
        Assert.Equal(result.Xml.Root!.Elements(Di + "BPMNDiagram").Count() - 1, process.Elements(Model + "subProcess").Count());
    }

    /// <summary>
    /// A flow that goes back is a pair of link events — BPMN's off-page connector — not a line dragged back across the
    /// whole map: every "… aşamasına dön" has its "… aşamasına dönüş", which leads into the stage it names.
    /// </summary>
    [Theory]
    [MemberData(nameof(Maps))]
    public void A_Flow_Back_Is_A_Pair_Of_Link_Events(string map)
    {
        XElement process = ProcessOf(Map(map).Xml);
        List<XElement> thrown = [.. process.Elements(Model + "intermediateThrowEvent").Where(element => element.Element(Model + "linkEventDefinition") is not null)];
        Dictionary<string, XElement> caught = process.Elements(Model + "intermediateCatchEvent")
            .ToDictionary(element => element.Element(Model + "linkEventDefinition")!.Attribute("name")!.Value, StringComparer.Ordinal);

        Assert.NotEmpty(thrown);
        foreach (XElement link in thrown)
        {
            string name = link.Element(Model + "linkEventDefinition")!.Attribute("name")!.Value;
            XElement target = caught[name];
            XElement flow = Assert.Single(process.Elements(Model + "sequenceFlow"), flow => flow.Attribute("sourceRef")!.Value == target.Attribute("id")!.Value);
            XElement stage = process.Elements().Single(element => element.Attribute("id")?.Value == flow.Attribute("targetRef")!.Value);
            Assert.Equal(name, stage.Attribute("name")!.Value);
            Assert.Equal($"\"{name}\" aşamasına dön", link.Attribute("name")!.Value);
        }
    }

    /// <summary>No flow runs beneath the map any more: nothing is drawn below the lowest shape.</summary>
    [Theory]
    [MemberData(nameof(Maps))]
    public void No_Flow_Runs_Beneath_The_Shapes(string map)
    {
        XElement plane = MainPlane(Map(map).Xml);
        double bottom = Shapes(plane).Max(shape => shape.Bottom);

        Assert.All(Segments(plane), segment => Assert.True(Math.Max(segment.From.Y, segment.To.Y) <= bottom, $"{segment.Of} runs below the map."));
    }

    /// <summary>A message a stage sends is drawn — and an outcome that only sends one is no longer left out.</summary>
    [Fact]
    public void A_Message_Is_Drawn_On_Its_Outcome()
    {
        XElement process = ProcessOf(Build().Xml);

        XElement message = Assert.Single(process.Elements(Model + "intermediateThrowEvent"), element => element.Element(Model + "messageEventDefinition") is not null);
        Assert.Equal("SMS gönderilir", message.Attribute("name")!.Value);
        Assert.Equal("Talebiniz alındı.", message.Element(Model + "documentation")!.Value);
        Assert.Contains(process.Elements(Model + "sequenceFlow"), flow => flow.Attribute("targetRef")!.Value == message.Attribute("id")!.Value
            && flow.Attribute("name")!.Value == StageOutcome.Success);
    }

    /// <summary>Two different stages of one name are told apart, so a reader can match each box to its sheet row.</summary>
    [Fact]
    public void Two_Stages_Of_One_Name_Are_Told_Apart()
    {
        StageMapResult result = BuildCampaign();

        Assert.Equal("BAŞKA EKİBE Mİ? (1)", result.Stages.Single(stage => stage.Id == Step(2)).Label);
        Assert.Equal("BAŞKA EKİBE Mİ? (2)", result.Stages.Single(stage => stage.Id == Step(8)).Label);
        Assert.Equal(result.Stages.Count, result.Stages.Select(stage => stage.Label).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The map does not claim a case closes where the stage record only says nothing follows: an end says which it is.
    /// </summary>
    [Fact]
    public void An_End_Says_What_Is_Known()
    {
        XElement process = ProcessOf(Build().Xml);
        List<string> ends = [.. process.Elements(Model + "endEvent").Select(end => end.Attribute("name")!.Value)];

        Assert.Contains("Sonraki aşama yok", ends);
        Assert.Contains("Aşamanın çıkışı tanımlı değil", ends);
        Assert.Contains("Aşama bulunamadı: ESKİ AŞAMA", ends);
        Assert.DoesNotContain("Bitiş", ends);
        Assert.Equal("Talep açılır", process.Element(Model + "startEvent")!.Attribute("name")!.Value);
    }

    /// <summary>A workflow the run does not have is still on the map, saying so, rather than silently missing.</summary>
    [Fact]
    public void A_Workflow_This_Run_Does_Not_Have_Says_So()
    {
        StageMapResult result = Build();
        XElement sub = ProcessOf(result.Xml).Elements(Model + "subProcess").Single(element => element.Attribute("name")!.Value == "IPTAL_BILDIR");

        Assert.Contains("bulunamadı", sub.Element(Model + "documentation")!.Value, StringComparison.Ordinal);
        Assert.Equal(1, result.MissingWorkflows);
    }

    /// <summary>
    /// The rules a stage carries beside its transitions are in its documentation, from the one shared table — and
    /// where it goes is drawn, so it is not said again there; nor is the name of a CRM table.
    /// </summary>
    [Fact]
    public void A_Stage_Carries_Its_Rules()
    {
        XElement task = ProcessOf(Build().Xml).Elements().Single(element => element.Attribute("name")?.Value == "CRM EVRAK KONTROL");
        string documentation = task.Element(Model + "documentation")!.Value;

        Assert.Contains("SLA periyodu: 2.", documentation, StringComparison.Ordinal);
        Assert.Contains("Kuyruk: Emeklilik Operasyon.", documentation, StringComparison.Ordinal);
        Assert.DoesNotContain("olumlu:", documentation, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void The_Map_Names_No_Crm_Table(string map)
    {
        Assert.DoesNotContain("ps_step", Map(map).Xml.ToString(), StringComparison.Ordinal);
    }
}
