using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;
using Crm.Ir.Stages;

namespace Crm.Bpmn.Stages;

/// <summary>What one stage map drew: the stages on it, and the workflows embedded or missing.</summary>
public sealed record StageMapResult(XDocument Xml, IReadOnlyList<Guid> Stages, int Workflows, int MissingWorkflows);

/// <summary>
/// The process a case of one subcategory goes through, drawn from its primary stage: every stage reachable from it,
/// the outcome that leads to each, and the workflows fired on the way — each embedded whole.
///
/// <para>
/// THE STAGES ARE A GRAPH, NOT A TREE. "Evrak bekleniyor" sends a case back to "Evrak kontrol", a stage can be
/// reached by several outcomes of several others, and a cancellation can loop a case to where it started. The block
/// layout that draws a workflow assumes a tree and would draw a loop as a duplicate, so this is laid out in layers
/// instead: each stage as far right as its longest path from the start, loops drawn as flows that go back.
/// </para>
///
/// <para>
/// EVERY ROUTE IS KEPT OFF EVERY SHAPE BY CONSTRUCTION rather than checked afterwards. Upright runs only ever happen
/// in the gaps between layers, which hold no shapes; a flow that goes back or skips a layer runs in its own lane
/// beneath the whole diagram; and a stage's three outcomes leave its diamond from three different corners —
/// success to the right, failure from below, cancellation from above — so their first runs, and their captions,
/// never share a line.
/// </para>
///
/// <para>
/// A WORKFLOW IS EMBEDDED, NOT LINKED. Each one is a collapsed sub-process carrying that workflow's own diagram, so
/// the map is one file a reader can drill into, and a viewer that cannot drill still shows a named box. A workflow
/// fired from two places is embedded twice, with its ids prefixed apart, because a BPMN element lives in one place.
/// </para>
/// </summary>
public static class StageMap
{
    private const double Left = 40;
    private const double ColumnGap = 100;      // a caption's width, plus a little, before the first upright run
    private const double SlotSpacing = 14;     // between upright runs in one gap
    private const double GapMargin = 20;
    private const double RowGap = 40;
    private const double LaneTop = 40;
    private const double LaneSpacing = 16;
    private const double CornerDrop = 30;      // how far a flow leaving the top or bottom corner goes before it turns
    private const double CellMargin = 60;      // above and below a diamond: the corner runs and their captions
    private const double TaskToGateway = 30;
    private const double GatewaySize = 50;
    private const double EventSize = 36;

    private static readonly XNamespace Model = BpmnSerializer.Model;
    private static readonly XNamespace Di = BpmnSerializer.Di;
    private static readonly XNamespace Dc = BpmnSerializer.Dc;
    private static readonly XNamespace DdDi = BpmnSerializer.DdDi;
    private static readonly XNamespace Xsi = BpmnSerializer.Xsi;

    private static readonly string[] Referring = ["id", "sourceRef", "targetRef", "default", "bpmnElement", "attachedToRef"];

    public static StageMapResult Build(CaseStage primary, IReadOnlyDictionary<Guid, CaseStage> stages,
        Func<Guid, XDocument?> workflowDiagram, IReadOnlyDictionary<Guid, string> workflowFiles, string toolVersion)
    {
        Plan plan = Plan.From(primary, stages);
        Layout(plan);

        string processId = "stagemap_" + primary.Id.ToString("N");
        string title = primary.Process.Length > 0 ? primary.Process : primary.Name;
        XElement process = new(Model + "process", new XAttribute("id", processId), new XAttribute("name", title),
            new XAttribute("isExecutable", "false"),
            new XElement(Model + "documentation", $"Aşama akışı: {title}. Başlangıç aşaması: {primary.Name}. "
                + "Pensionsoft aşama kayıtlarından (ps_step) üretilmiş açıklayıcı modeldir; çalıştırılabilir değildir."));
        XElement plane = new(Di + "BPMNPlane", new XAttribute("id", "plane_" + processId), new XAttribute("bpmnElement", processId));
        List<XElement> drilldowns = [];
        int embedded = 0;
        int missing = 0;

        foreach (Unit unit in plan.Units)
        {
            foreach (XElement element in Elements(unit, workflowDiagram, workflowFiles, drilldowns, ref embedded, ref missing))
            {
                process.Add(element);
            }
            foreach (XElement shape in Shapes(unit))
            {
                plane.Add(shape);
            }
        }
        foreach (Unit cell in plan.Units.Where(unit => unit.HasGateway))
        {
            string flowId = cell.TaskId + "_to_gateway";
            process.Add(new XElement(Model + "sequenceFlow", new XAttribute("id", flowId),
                new XAttribute("sourceRef", cell.TaskId), new XAttribute("targetRef", cell.GatewayId)));
            plane.Add(Edge(flowId, [(cell.TaskRight, cell.SpineY), (cell.GatewayX, cell.SpineY)], null));
        }
        foreach (Link link in plan.Links)
        {
            XElement flow = new(Model + "sequenceFlow", new XAttribute("id", link.Id),
                new XAttribute("sourceRef", link.From.ExitId), new XAttribute("targetRef", link.To.EntryId));
            if (link.Outcome is string outcome)
            {
                flow.Add(new XAttribute("name", outcome));
                if (link.From.HasGateway)
                {
                    flow.Add(new XElement(Model + "conditionExpression", new XAttribute(Xsi + "type", "tFormalExpression"), outcome));
                }
            }
            process.Add(flow);
            plane.Add(Edge(link.Id, link.Points, link.Caption));
        }

        (XElement note, XElement association, XElement noteShape, XElement noteEdge) = Note(plan, processId, title, primary, embedded, missing);
        process.Add(note, association);
        plane.Add(noteShape, noteEdge);

        XElement definitions = new(Model + "definitions",
            new XAttribute("xmlns", Model.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "bpmndi", Di),
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(XNamespace.Xmlns + "di", DdDi),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(XNamespace.Xmlns + "cwe", BpmnSerializer.Provenance),
            new XAttribute("id", "def_" + processId),
            new XAttribute("targetNamespace", "urn:crm-workflow-extractor:processes"),
            new XAttribute("exporter", "CrmWorkflowExtractor"),
            new XAttribute("exporterVersion", toolVersion),
            process,
            new XElement(Di + "BPMNDiagram", new XAttribute("id", "diagram_" + processId), plane),
            drilldowns);
        XDocument document = BpmnSerializer.Sweep(new XDocument(new XDeclaration("1.0", "UTF-8", null), definitions));
        return new StageMapResult(document, [.. plan.Units.Where(unit => unit.Stage is not null).Select(unit => unit.Stage!.Id)], embedded, missing);
    }

    private static IEnumerable<XElement> Elements(Unit unit, Func<Guid, XDocument?> workflowDiagram,
        IReadOnlyDictionary<Guid, string> workflowFiles, List<XElement> drilldowns, ref int embedded, ref int missing)
    {
        List<XElement> elements = [];
        if (unit.Kind == UnitKind.Start)
        {
            elements.Add(new XElement(Model + "startEvent", new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name)));
            return elements;
        }
        if (unit.Kind == UnitKind.End)
        {
            elements.Add(new XElement(Model + "endEvent", new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name)));
            return elements;
        }
        if (unit.Kind == UnitKind.Workflow)
        {
            elements.Add(SubProcess(unit, workflowDiagram, workflowFiles, drilldowns, ref embedded, ref missing));
            return elements;
        }

        CaseStage stage = unit.Stage!;
        elements.Add(new XElement(Model + (stage.Automatic ? "serviceTask" : "userTask"),
            new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name),
            new XElement(Model + "documentation", Documentation(stage))));
        if (unit.HasGateway)
        {
            elements.Add(new XElement(Model + "exclusiveGateway", new XAttribute("id", unit.GatewayId)));
        }
        return elements;
    }

    /// <summary>
    /// A workflow as a collapsed sub-process carrying its own diagram. Its process content moves in, its drawing
    /// becomes a plane of its own, and every id in both is prefixed with this occurrence's, so a workflow fired from
    /// two places is two elements rather than one element in two places.
    /// </summary>
    private static XElement SubProcess(Unit unit, Func<Guid, XDocument?> workflowDiagram,
        IReadOnlyDictionary<Guid, string> workflowFiles, List<XElement> drilldowns, ref int embedded, ref int missing)
    {
        Guid workflow = unit.WorkflowId!.Value;
        string file = workflowFiles.TryGetValue(workflow, out string? path) ? $" Kendi diyagramı: bpmn/{path}.bpmn." : "";
        XElement subProcess = new(Model + "subProcess", new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name));
        if (workflowDiagram(workflow) is not XDocument diagram
            || diagram.Root?.Element(Model + "process") is not XElement source
            || diagram.Descendants(Di + "BPMNPlane").FirstOrDefault() is not XElement drawing)
        {
            missing++;
            subProcess.Add(new XElement(Model + "documentation",
                $"İş akışı {unit.Name} ({workflow:D}) bu çalıştırmada bulunamadı: silinmiş, taslak ya da dışa aktarımın dışında kalmış olabilir."));
            return subProcess;
        }

        embedded++;
        string prefix = unit.TaskId + "_";
        subProcess.Add(new XElement(Model + "documentation", $"İş akışı: {unit.Name} ({workflow:D}).{file}"));
        foreach (XElement child in source.Elements().Where(child => child.Name != Model + "documentation" && child.Name != Model + "extensionElements"))
        {
            subProcess.Add(Prefixed(child, prefix));
        }
        XElement innerPlane = new(Di + "BPMNPlane", new XAttribute("id", "plane_" + unit.TaskId), new XAttribute("bpmnElement", unit.TaskId),
            drawing.Elements().Select(child => Prefixed(child, prefix)));
        drilldowns.Add(new XElement(Di + "BPMNDiagram", new XAttribute("id", "diagram_" + unit.TaskId), innerPlane));
        return subProcess;
    }

    private static XElement Prefixed(XElement element, string prefix)
    {
        XElement copy = new(element);
        foreach (XElement node in copy.DescendantsAndSelf())
        {
            foreach (string name in Referring)
            {
                if (node.Attribute(name) is XAttribute reference)
                {
                    reference.Value = prefix + reference.Value;
                }
            }
        }
        return copy;
    }

    /// <summary>What a stage does besides moving on: its outcomes, its messages and every rule it carries.</summary>
    private static string Documentation(CaseStage stage)
    {
        List<string> parts = [$"Aşama: {stage.Name} ({stage.Id:D}).", $"Süreç (konu): {stage.Process}."];
        parts.Add(stage.Primary ? "Birincil aşama." : "Birincil aşama değil.");
        parts.Add(stage.Automatic ? "Otomatik aşama." : "Kullanıcı aşaması.");
        if (!stage.Active)
        {
            parts.Add("Bu aşama PASİF.");
        }
        foreach (StageExit exit in stage.Exits)
        {
            List<string> what = [];
            if (exit.NextStageName is string next)
            {
                what.Add("sonraki aşama " + next);
            }
            if (exit.WorkflowName is string workflow)
            {
                what.Add("iş akışı " + workflow);
            }
            if (exit.Sms is string sms)
            {
                what.Add($"SMS \"{sms}\"");
            }
            parts.Add($"{exit.Outcome}: {string.Join(", ", what)}.");
        }
        if (stage.Fields.ContainsKey(CaseStageRules.AssignedUser))
        {
            parts.Add("Belirli bir kullanıcıya atanır.");
        }
        foreach (CaseStageRule rule in CaseStageRules.All)
        {
            if (stage.Fields.TryGetValue(rule.Field, out string? value))
            {
                parts.Add($"{rule.Label}: {value}.");
            }
        }
        return string.Join(" ", parts);
    }

    private static IEnumerable<XElement> Shapes(Unit unit)
    {
        List<XElement> shapes = [];
        XElement shape = new(Di + "BPMNShape", new XAttribute("id", "shape_" + unit.TaskId), new XAttribute("bpmnElement", unit.TaskId),
            Bounds(unit.X, unit.ShapeTop, unit.ShapeWidth, unit.ShapeHeight));
        if (unit.Kind == UnitKind.Workflow)
        {
            shape.Add(new XAttribute("isExpanded", "false"));
        }
        if (unit.Kind is UnitKind.Start or UnitKind.End && unit.Name.Length > 0)
        {
            (double width, double height) = LabelBox.Measure(unit.Name);
            shape.Add(new XElement(Di + "BPMNLabel",
                Bounds(unit.X + (EventSize / 2) - (width / 2), unit.ShapeTop + EventSize + LabelBox.Gap, width, height)));
        }
        shapes.Add(shape);
        if (unit.HasGateway)
        {
            shapes.Add(new XElement(Di + "BPMNShape", new XAttribute("id", "shape_" + unit.GatewayId),
                new XAttribute("bpmnElement", unit.GatewayId), new XAttribute("isMarkerVisible", "true"),
                Bounds(unit.GatewayX, unit.SpineY - (GatewaySize / 2), GatewaySize, GatewaySize)));
        }
        return shapes;
    }

    private static XElement Edge(string id, IReadOnlyList<(double X, double Y)> points, (double X, double Y, double Width, double Height)? caption)
    {
        XElement edge = new(Di + "BPMNEdge", new XAttribute("id", "edge_" + id), new XAttribute("bpmnElement", id),
            points.Select(point => new XElement(DdDi + "waypoint", Number("x", point.X), Number("y", point.Y))));
        if (caption is (double x, double y, double width, double height))
        {
            edge.Add(new XElement(Di + "BPMNLabel", Bounds(x, y, width, height)));
        }
        return edge;
    }

    private static (XElement Note, XElement Association, XElement Shape, XElement Edge) Note(Plan plan, string processId,
        string title, CaseStage primary, int embedded, int missing)
    {
        int stages = plan.Units.Count(unit => unit.Stage is not null);
        string text = string.Join("\n",
            $"{title}",
            $"Aşama akışı · başlangıç: {primary.ShortName}",
            $"{stages} aşama · {embedded} iş akışı gömülü" + (missing > 0 ? $" · {missing} iş akışı bulunamadı" : ""),
            "Sonuçlar: olumlu sağa, olumsuz aşağıdan, iptal yukarıdan çıkar.",
            "İş akışı kutularının içine girilerek adımları görülebilir.",
            "Pensionsoft aşama kayıtlarından (ps_step) üretilmiş açıklayıcı modeldir. Çalıştırılabilir değildir.");
        string noteId = "note_" + processId;
        Unit start = plan.Units[0];
        XElement note = new(Model + "textAnnotation", new XAttribute("id", noteId), new XElement(Model + "text", text));
        XElement association = new(Model + "association", new XAttribute("id", noteId + "_link"),
            new XAttribute("sourceRef", noteId), new XAttribute("targetRef", start.TaskId));
        XElement shape = new(Di + "BPMNShape", new XAttribute("id", "shape_" + noteId), new XAttribute("bpmnElement", noteId),
            Bounds(Left, Left, 560, plan.NoteHeight));
        XElement edge = new(Di + "BPMNEdge", new XAttribute("id", "edge_" + noteId + "_link"), new XAttribute("bpmnElement", noteId + "_link"),
            new XElement(DdDi + "waypoint", Number("x", Left + 40), Number("y", Left + plan.NoteHeight)),
            new XElement(DdDi + "waypoint", Number("x", start.X + (EventSize / 2)), Number("y", start.ShapeTop)));
        return (note, association, shape, edge);
    }

    private static XElement Bounds(double x, double y, double width, double height)
    {
        return new XElement(Dc + "Bounds", Number("x", x), Number("y", y), Number("width", width), Number("height", height));
    }

    private static XAttribute Number(string name, double value)
    {
        return new XAttribute(name, Math.Round(value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Layers by the longest path from the start over the flows that go FORWARD — a loop's flow back is found by a
    /// depth-first walk and left out of the count, or the layering would never end — then columns, rows and routes.
    /// </summary>
    private static void Layout(Plan plan)
    {
        Dictionary<Unit, int> state = [];
        HashSet<Link> back = [];
        Walk(plan.Units[0], plan, state, back);
        foreach (Unit unit in plan.Units)
        {
            unit.Layer = 0;
        }
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Link link in plan.Links.Where(link => !back.Contains(link)))
            {
                if (link.To.Layer < link.From.Layer + 1)
                {
                    link.To.Layer = link.From.Layer + 1;
                    changed = true;
                }
            }
        }

        int layers = plan.Units.Max(unit => unit.Layer) + 1;
        double[] widths = new double[layers];
        int[] slots = new int[layers];
        foreach (Unit unit in plan.Units)
        {
            widths[unit.Layer] = Math.Max(widths[unit.Layer], unit.Width);
        }
        foreach (Link link in plan.Links)
        {
            link.OutSlot = slots[link.From.Layer]++;
            link.Adjacent = link.To.Layer == link.From.Layer + 1;
            if (!link.Adjacent)
            {
                link.InSlot = slots[link.To.Layer - 1]++;
            }
        }
        double[] columns = new double[layers];
        double[] gaps = new double[layers];
        for (int layer = 0; layer < layers; layer++)
        {
            gaps[layer] = ColumnGap + (Math.Max(1, slots[layer]) * SlotSpacing) + GapMargin;
            columns[layer] = layer == 0 ? Left : columns[layer - 1] + widths[layer - 1] + gaps[layer - 1];
        }

        double top = Left + plan.NoteHeight + 60;
        foreach (IGrouping<int, Unit> layer in plan.Units.Where(unit => unit.Kind != UnitKind.Start).GroupBy(unit => unit.Layer))
        {
            double y = top;
            foreach (Unit unit in layer.OrderBy(unit => unit.Index))
            {
                unit.X = columns[unit.Layer];
                unit.Y = y;
                y += unit.Height + RowGap;
            }
        }
        Unit start = plan.Units[0];
        Unit first = plan.Units[1];
        start.X = columns[0];
        start.Y = first.SpineY - (EventSize / 2);

        double bottom = plan.Units.Max(unit => unit.Y + unit.Height);
        int lane = 0;
        foreach (Link link in plan.Links)
        {
            double slotOut = columns[link.From.Layer] + widths[link.From.Layer] + ColumnGap + (link.OutSlot * SlotSpacing) + (SlotSpacing / 2);
            (List<(double X, double Y)> points, double runY, double runStart, bool below) = Leave(link);
            points.Add((slotOut, runY));
            double targetY = link.To.SpineY;
            if (link.Adjacent)
            {
                points.Add((slotOut, targetY));
            }
            else
            {
                double laneY = bottom + LaneTop + (lane++ * LaneSpacing);
                double slotIn = columns[link.To.Layer - 1] + widths[link.To.Layer - 1] + ColumnGap + (link.InSlot * SlotSpacing) + (SlotSpacing / 2);
                points.Add((slotOut, laneY));
                points.Add((slotIn, laneY));
                points.Add((slotIn, targetY));
            }
            points.Add((link.To.X, targetY));
            link.Points = [.. points.Where((point, index) => index == 0 || point != points[index - 1])];
            if (link.Outcome is string outcome)
            {
                (double width, double height) = LabelBox.Measure(outcome);
                double y = below ? runY + LabelBox.Gap : runY - LabelBox.Gap - height;
                link.Caption = (runStart + 4, y, width, height);
            }
        }
    }

    /// <summary>
    /// Where a flow leaves its unit, up to the start of its first level run. Out of a diamond, each outcome has a
    /// corner of its own, so no two outcomes ever share a line or a caption.
    /// </summary>
    private static (List<(double X, double Y)> Points, double RunY, double RunStart, bool Below) Leave(Link link)
    {
        Unit from = link.From;
        if (!from.HasGateway)
        {
            return ([(from.ShapeRight, from.SpineY)], from.SpineY, from.ShapeRight, false);
        }
        double centre = from.GatewayX + (GatewaySize / 2);
        double half = GatewaySize / 2;
        if (link.Outcome == StageOutcome.Failure)
        {
            double y = from.SpineY + CornerDrop;
            return ([(centre, from.SpineY + half), (centre, y)], y, centre, true);
        }
        if (link.Outcome == StageOutcome.Cancel)
        {
            double y = from.SpineY - CornerDrop;
            return ([(centre, from.SpineY - half), (centre, y)], y, centre, false);
        }
        return ([(from.GatewayX + GatewaySize, from.SpineY)], from.SpineY, from.GatewayX + GatewaySize, false);
    }

    private static void Walk(Unit unit, Plan plan, Dictionary<Unit, int> state, HashSet<Link> back)
    {
        state[unit] = 1;
        foreach (Link link in plan.Links.Where(link => link.From == unit))
        {
            int seen = state.GetValueOrDefault(link.To);
            if (seen == 1)
            {
                back.Add(link);
                continue;
            }
            if (seen == 0)
            {
                Walk(link.To, plan, state, back);
            }
        }
        state[unit] = 2;
    }

    private enum UnitKind
    {
        Start,
        Cell,
        Workflow,
        End
    }

    /// <summary>
    /// One thing laid out as a block: the start, a stage (its task and, when it has more than one way out, its
    /// diamond), an embedded workflow, or an end. A unit's box includes the room its captions and corner runs need,
    /// so units stacked in a column cannot reach each other.
    /// </summary>
    private sealed class Unit(int index, UnitKind kind, string name)
    {
        public int Index { get; } = index;

        public UnitKind Kind { get; } = kind;

        public string Name { get; } = name;

        public CaseStage? Stage { get; init; }

        public Guid? WorkflowId { get; init; }

        public bool HasGateway { get; set; }

        public int Layer { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public string TaskId
        {
            get { return "u" + Index.ToString(CultureInfo.InvariantCulture); }
        }

        public string GatewayId
        {
            get { return TaskId + "_gw"; }
        }

        public string EntryId
        {
            get { return TaskId; }
        }

        public string ExitId
        {
            get { return HasGateway ? GatewayId : TaskId; }
        }

        public double ShapeWidth
        {
            get { return Kind is UnitKind.Start or UnitKind.End ? EventSize : FlowNode.Size(FlowNodeType.ServiceTask, Name).Width; }
        }

        public double ShapeHeight
        {
            get { return Kind is UnitKind.Start or UnitKind.End ? EventSize : FlowNode.Size(FlowNodeType.ServiceTask, Name).Height; }
        }

        public double Width
        {
            get { return ShapeWidth + (HasGateway ? TaskToGateway + GatewaySize : 0); }
        }

        /// <summary>The shape, its text below it where the text is drawn outside, and room for corner runs.</summary>
        public double Height
        {
            get
            {
                if (Kind is UnitKind.Start or UnitKind.End)
                {
                    return EventSize + LabelBox.Gap + LabelBox.Measure(Name).Height;
                }
                return HasGateway ? Math.Max(ShapeHeight, GatewaySize) + (CellMargin * 2) : ShapeHeight;
            }
        }

        /// <summary>The height the unit's flows come in and go out at.</summary>
        public double SpineY
        {
            get
            {
                if (Kind is UnitKind.Start or UnitKind.End)
                {
                    return Y + (EventSize / 2);
                }
                return Y + (HasGateway ? CellMargin + (Math.Max(ShapeHeight, GatewaySize) / 2) : ShapeHeight / 2);
            }
        }

        public double ShapeTop
        {
            get { return SpineY - (ShapeHeight / 2); }
        }

        public double TaskRight
        {
            get { return X + ShapeWidth; }
        }

        public double ShapeRight
        {
            get { return X + ShapeWidth; }
        }

        public double GatewayX
        {
            get { return X + ShapeWidth + TaskToGateway; }
        }
    }

    private sealed class Link(int index, Unit from, Unit to, string? outcome)
    {
        public string Id { get; } = "f" + index.ToString(CultureInfo.InvariantCulture);

        public Unit From { get; } = from;

        public Unit To { get; } = to;

        public string? Outcome { get; } = outcome;

        public int OutSlot { get; set; }

        public int InSlot { get; set; }

        public bool Adjacent { get; set; }

        public IReadOnlyList<(double X, double Y)> Points { get; set; } = [];

        public (double X, double Y, double Width, double Height)? Caption { get; set; }
    }

    /// <summary>The units and flows of one map, found by walking from the primary stage in the order a case would.</summary>
    private sealed class Plan
    {
        public List<Unit> Units { get; } = [];

        public List<Link> Links { get; } = [];

        public double NoteHeight { get; private set; }

        public static Plan From(CaseStage primary, IReadOnlyDictionary<Guid, CaseStage> stages)
        {
            Plan plan = new();
            Unit start = plan.Add(UnitKind.Start, "Başlangıç");
            Dictionary<Guid, Unit> cells = [];
            Queue<CaseStage> queue = new();
            plan.Link(start, plan.Cell(primary, primary, cells, queue), null);
            while (queue.TryDequeue(out CaseStage? stage))
            {
                Unit cell = cells[stage.Id];
                List<(Unit Target, string Outcome)> exits = [];
                foreach (StageExit exit in stage.Exits.Where(exit => exit.NextStage is not null || exit.Workflow is not null))
                {
                    Unit? next = exit.NextStage is Guid id
                        ? stages.TryGetValue(id, out CaseStage? found) ? plan.Cell(found, primary, cells, queue) : plan.Add(UnitKind.End, "Aşama bulunamadı: " + (exit.NextStageName ?? id.ToString("D")))
                        : null;
                    if (exit.Workflow is Guid workflow)
                    {
                        Unit fired = plan.Add(UnitKind.Workflow, exit.WorkflowName ?? workflow.ToString("D"), workflow: workflow);
                        exits.Add((fired, exit.Outcome));
                        plan.Link(fired, next ?? plan.Add(UnitKind.End, "Bitiş"), null);
                        continue;
                    }
                    exits.Add((next!, exit.Outcome));
                }
                if (exits.Count == 0)
                {
                    plan.Link(cell, plan.Add(UnitKind.End, "Bitiş"), null);
                    continue;
                }
                cell.HasGateway = exits.Count > 1;
                foreach ((Unit target, string outcome) in exits)
                {
                    plan.Link(cell, target, outcome);
                }
            }
            plan.NoteHeight = 6 * 15 + 16;
            return plan;
        }

        private Unit Cell(CaseStage stage, CaseStage primary, Dictionary<Guid, Unit> cells, Queue<CaseStage> queue)
        {
            if (cells.TryGetValue(stage.Id, out Unit? existing))
            {
                return existing;
            }
            string name = stage.ShortName;
            if (stage.Process != primary.Process && stage.Process.Length > 0)
            {
                name += $" [{stage.Process}]";
            }
            if (!stage.Active)
            {
                name += " (pasif)";
            }
            Unit cell = Add(UnitKind.Cell, name, stage);
            cells[stage.Id] = cell;
            queue.Enqueue(stage);
            return cell;
        }

        private Unit Add(UnitKind kind, string name, CaseStage? stage = null, Guid? workflow = null)
        {
            Unit unit = new(Units.Count, kind, name) { Stage = stage, WorkflowId = workflow };
            Units.Add(unit);
            return unit;
        }

        private void Link(Unit from, Unit to, string? outcome)
        {
            Links.Add(new Link(Links.Count, from, to, outcome));
        }
    }
}
