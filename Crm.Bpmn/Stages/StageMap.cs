using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;
using Crm.Ir.Stages;
using Crm.Ir.Text;

namespace Crm.Bpmn.Stages;

/// <summary>A stage as one map draws it: the text on its box, which is what a reader searches the file for.</summary>
public sealed record MapStage(Guid Id, string Label);

/// <summary>What one stage map drew: its stages, its distinct workflows, the boxes they took and those missing.</summary>
public sealed record StageMapResult(XDocument Xml, IReadOnlyList<MapStage> Stages, int Workflows, int Boxes, int MissingWorkflows);

/// <summary>
/// The process a case of one subcategory goes through, drawn from its primary stage: every stage reachable from it,
/// the outcome that leads to each, and the workflows fired and the messages sent on the way.
///
/// <para>
/// THE STAGES ARE A GRAPH, NOT A TREE. "Evrak bekleniyor" sends a case back to "Evrak kontrol", a stage can be
/// reached by several outcomes of several others, and a cancellation can loop a case to where it started. So this is
/// laid out in layers: each stage as far right as its longest path from the start. A flow that goes BACK is not drawn
/// back across the map: it ends in a link event, "… aşamasına dön", which the matching "… aşamasına dönüş" picks up
/// beside the stage it returns to — BPMN's own off-page connector. On the Reklam map of 2026-10-12 the flows back ran
/// in lanes under everything, up to 7,958 pixels long, and made more than half of its 52 crossings.
/// </para>
///
/// <para>
/// EVERY ROUTE IS KEPT OFF EVERY SHAPE BY CONSTRUCTION rather than checked afterwards. Every flow runs between
/// neighbouring layers; a flow that skips layers passes through each one on a row of its own that holds no shape.
/// Upright runs happen only in the gaps between layers, which hold no shapes, in an order chosen to cross as little
/// as possible, and flows into one shape come in at heights of their own so they never share a line. A stage's
/// outcomes leave its diamond from different corners — success to the right, failure from below, cancellation from
/// above — so their first runs, and their captions, never share a line either.
/// </para>
///
/// <para>
/// OUTCOMES THAT AGREE ARE ONE FLOW. Where a stage's outcomes lead to the same stage through the same workflow with
/// the same message, the map draws one flow named for all of them ("olumsuz / iptal"); a diamond whose branches
/// all go to one place decides nothing and reads as a modelling error. A workflow fired whichever way the stage
/// closes is drawn once, between the stage and its diamond.
/// </para>
///
/// <para>
/// A WORKFLOW IS EMBEDDED ONCE PER MAP. Its first box is a collapsed sub-process carrying the workflow's own diagram
/// on a plane of its own, so the map is one file to drill into; every later box for the same workflow is a call
/// activity — BPMN's notation for reuse — that says where the first one is. Embedding every occurrence made one
/// workflow fourteen copies on one map and most of a 2.6 MB file. Ids come from CRM's own ids, so a map compares
/// from one run to the next.
/// </para>
/// </summary>
public static class StageMap
{
    private const double Left = 40;
    private const double ColumnGap = 100;      // a caption's width, plus a little, before the first upright run
    private const double SlotSpacing = 14;     // between upright runs in one gap
    private const double GapMargin = 32;       // past the last upright run: an event's label overhangs its column by 27
    private const double RowGap = 40;
    private const double CornerDrop = 30;      // how far a flow leaving the top or bottom corner goes, at least, before it turns
    private const double CellMargin = 60;      // above and below a diamond, at least: the corner runs and their captions
    private const double TaskToGateway = 30;
    private const double GatewaySize = 50;
    private const double EventSize = 36;
    private const double NoteWidth = 560;
    private const int NoteLineLength = 85;
    private const int NoteWorkflows = 12;

    private static readonly XNamespace Model = BpmnSerializer.Model;
    private static readonly XNamespace Di = BpmnSerializer.Di;
    private static readonly XNamespace Dc = BpmnSerializer.Dc;
    private static readonly XNamespace DdDi = BpmnSerializer.DdDi;
    private static readonly XNamespace Xsi = BpmnSerializer.Xsi;

    private static readonly string[] Referring = ["id", "sourceRef", "targetRef", "default", "bpmnElement", "attachedToRef"];
    private static readonly string[] Outcomes = [StageOutcome.Success, StageOutcome.Failure, StageOutcome.Cancel];

    public static StageMapResult Build(CaseStage primary, IReadOnlyDictionary<Guid, CaseStage> stages,
        Func<Guid, XDocument?> workflowDiagram, IReadOnlyDictionary<Guid, string> workflowFiles, string toolVersion)
    {
        string processId = "stagemap_" + primary.Id.ToString("N");
        string title = primary.Process.Length > 0 ? primary.Process : primary.Name;
        Plan plan = Plan.From(primary, stages);
        Layer(plan);
        List<string> lines = NoteLines(plan, title, workflowDiagram);
        plan.NoteHeight = NoteHeight(lines);
        Place(plan);
        XElement process = new(Model + "process", new XAttribute("id", processId), new XAttribute("name", title),
            new XAttribute("isExecutable", "false"),
            new XElement(Model + "documentation", $"Aşama haritası: {title}. Başlangıç aşaması: {primary.Name}. "
                + "CRM'deki aşama kayıtlarından üretilmiş açıklayıcı modeldir; çalıştırılabilir değildir."));
        XElement plane = new(Di + "BPMNPlane", new XAttribute("id", "plane_" + processId), new XAttribute("bpmnElement", processId));
        Boxes boxes = new(workflowDiagram, workflowFiles);

        // In reading order, so the box that carries a workflow's diagram is the first one a reader meets.
        foreach (Unit unit in plan.Units.Where(unit => unit.Kind != UnitKind.Pass).OrderBy(unit => unit.Layer).ThenBy(unit => unit.Y).ThenBy(unit => unit.Index))
        {
            foreach (XElement element in Elements(unit, boxes))
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
            List<(string Id, double From, double To)> legs = cell.Always is null
                ? [(cell.TaskId, cell.TaskRight, cell.GatewayX)]
                : [(cell.TaskId, cell.TaskRight, cell.AlwaysX), (cell.AlwaysId, cell.AlwaysX + cell.AlwaysWidth, cell.GatewayX)];
            string[] targets = cell.Always is null ? [cell.GatewayId] : [cell.AlwaysId, cell.GatewayId];
            for (int index = 0; index < legs.Count; index++)
            {
                string flowId = legs[index].Id + "_ileri";
                process.Add(new XElement(Model + "sequenceFlow", new XAttribute("id", flowId),
                    new XAttribute("sourceRef", legs[index].Id), new XAttribute("targetRef", targets[index])));
                plane.Add(Edge(flowId, [(legs[index].From, cell.SpineY), (legs[index].To, cell.SpineY)], null));
            }
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

        (XElement note, XElement association, XElement noteShape, XElement noteEdge) = Note(plan, processId, lines);
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
            boxes.Drilldowns);
        XDocument document = BpmnSerializer.Sweep(new XDocument(new XDeclaration("1.0", "UTF-8", null), definitions));
        List<MapStage> drawn = [.. plan.Units.Where(unit => unit.Stage is not null).Select(unit => new MapStage(unit.Stage!.Id, unit.Name))];
        return new StageMapResult(document, drawn, boxes.First.Count, boxes.Count, boxes.Missing);
    }

    private static List<XElement> Elements(Unit unit, Boxes boxes)
    {
        List<XElement> elements = [];
        if (unit.Kind == UnitKind.Start)
        {
            elements.Add(new XElement(Model + "startEvent", new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name)));
            return elements;
        }
        if (unit.Kind == UnitKind.End)
        {
            XElement end = new(Model + "endEvent", new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name));
            if (unit.Documentation is string said)
            {
                end.Add(new XElement(Model + "documentation", said));
            }
            elements.Add(end);
            return elements;
        }
        if (unit.Kind == UnitKind.Workflow)
        {
            elements.Add(boxes.Box(unit.TaskId, unit.Name, unit.WorkflowId!.Value, unit.FiredBy, out bool embedded));
            unit.Embedded = embedded;
            return elements;
        }
        if (unit.Kind == UnitKind.Message)
        {
            elements.Add(new XElement(Model + "intermediateThrowEvent", new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name),
                new XElement(Model + "documentation", unit.Documentation),
                new XElement(Model + "messageEventDefinition", new XAttribute("id", unit.TaskId + "_mesaj"))));
            return elements;
        }
        if (unit.Kind is UnitKind.LinkThrow or UnitKind.LinkCatch)
        {
            elements.Add(new XElement(Model + (unit.Kind == UnitKind.LinkThrow ? "intermediateThrowEvent" : "intermediateCatchEvent"),
                new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name),
                new XElement(Model + "linkEventDefinition", new XAttribute("id", unit.TaskId + "_baglanti"), new XAttribute("name", unit.LinkName!))));
            return elements;
        }

        CaseStage stage = unit.Stage!;
        elements.Add(new XElement(Model + (stage.Automatic ? "serviceTask" : "userTask"),
            new XAttribute("id", unit.TaskId), new XAttribute("name", unit.Name),
            new XElement(Model + "documentation", Documentation(stage))));
        if (unit.Always is (Guid workflow, string name))
        {
            elements.Add(boxes.Box(unit.AlwaysId, name, workflow, $"{unit.Name} — her sonuçta", out bool embedded));
            unit.Embedded = embedded;
        }
        if (unit.HasGateway)
        {
            elements.Add(new XElement(Model + "exclusiveGateway", new XAttribute("id", unit.GatewayId)));
        }
        return elements;
    }

    /// <summary>
    /// What a stage carries that the map does not draw: its rules — SLA, assignment, documents, closing. Where it goes
    /// on each outcome, and what it fires and sends, is drawn, so it is not said twice.
    /// </summary>
    private static string Documentation(CaseStage stage)
    {
        List<string> parts = [$"Aşama: {stage.Name} ({stage.Id:D})."];
        if (!stage.Active)
        {
            parts.Add("Bu aşama PASİF.");
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

    private static List<XElement> Shapes(Unit unit)
    {
        List<XElement> shapes = [];
        if (unit.Kind is UnitKind.Pass)
        {
            return shapes;
        }
        XElement shape = new(Di + "BPMNShape", new XAttribute("id", "shape_" + unit.TaskId), new XAttribute("bpmnElement", unit.TaskId),
            Bounds(unit.X, unit.ShapeTop, unit.ShapeWidth, unit.ShapeHeight));
        if (unit.Kind == UnitKind.Workflow && unit.Embedded)
        {
            shape.Add(new XAttribute("isExpanded", "false"));
        }
        if (unit.IsEvent && unit.Name.Length > 0)
        {
            (double width, double height) = LabelBox.Measure(unit.Name);
            shape.Add(new XElement(Di + "BPMNLabel",
                Bounds(unit.X + (EventSize / 2) - (width / 2), unit.ShapeTop + EventSize + LabelBox.Gap, width, height)));
        }
        shapes.Add(shape);
        if (unit.Always is (Guid, string name))
        {
            (double width, double height) = FlowNode.Size(FlowNodeType.ServiceTask, name);
            XElement box = new(Di + "BPMNShape", new XAttribute("id", "shape_" + unit.AlwaysId), new XAttribute("bpmnElement", unit.AlwaysId),
                Bounds(unit.AlwaysX, unit.SpineY - (height / 2), width, height));
            if (unit.Embedded)
            {
                box.Add(new XAttribute("isExpanded", "false"));
            }
            shapes.Add(box);
        }
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

    /// <summary>
    /// The map's note: what it is, how big, which workflows it fires and how often, and the conventions a BPMN reader
    /// would not guess — merged outcomes, link events, and the box that points at another box.
    /// </summary>
    private static List<string> NoteLines(Plan plan, string title, Func<Guid, XDocument?> workflowDiagram)
    {
        List<(Guid Id, string Name)> fired = [];
        foreach (Unit unit in plan.Units)
        {
            if (unit.Kind == UnitKind.Workflow)
            {
                fired.Add((unit.WorkflowId!.Value, unit.Name));
            }
            if (unit.Always is (Guid always, string name))
            {
                fired.Add((always, name));
            }
        }
        List<IGrouping<Guid, (Guid Id, string Name)>> distinct = [.. fired.GroupBy(box => box.Id)];
        int missing = distinct.Count(group => !Drawable(workflowDiagram(group.Key), out _, out _));
        int stages = plan.Units.Count(unit => unit.Stage is not null);
        List<string> lines = [title, $"Aşama haritası · başlangıç: {plan.Units[1].Name}",
            $"{stages} aşama · {distinct.Count} farklı iş akışı · {fired.Count} kutu" + (missing > 0 ? $" · {missing} iş akışı bulunamadı" : "")];
        if (distinct.Count > 0)
        {
            List<string> named = [.. distinct.OrderByDescending(group => group.Count())
                .Select(group => group.Count() > 1 ? $"{group.First().Name} ×{group.Count()}" : group.First().Name)];
            string more = named.Count > NoteWorkflows ? $" …ve {named.Count - NoteWorkflows} tane daha" : "";
            lines.Add("İş akışları: " + string.Join(", ", named.Take(NoteWorkflows)) + more);
        }
        lines.Add("Aynı yere giden sonuçlar tek okla gösterilir (ör. \"olumsuz / iptal\").");
        if (plan.Units.Any(unit => unit.Kind == UnitKind.LinkThrow))
        {
            lines.Add("Geri dönüşler bağlantı olaylarıyla gösterilir: \"… aşamasına dön\" okunu aynı adlı \"… aşamasına dönüş\" olayı karşılar.");
        }
        if (distinct.Count > 0)
        {
            lines.Add("Kutulara girilerek iş akışının adımları görülür; kalın kenarlı kutu, aynı iş akışının haritada ilk geçtiği kutuyu gösterir.");
        }
        lines.Add("Soru biçimindeki aşamalarda olumlu = evet, olumsuz = hayır.");
        return lines;
    }

    private static (XElement Note, XElement Association, XElement Shape, XElement Edge) Note(Plan plan, string processId, List<string> lines)
    {
        string text = string.Join("\n", lines);
        string noteId = "note_" + processId;
        Unit start = plan.Units[0];
        XElement note = new(Model + "textAnnotation", new XAttribute("id", noteId), new XElement(Model + "text", text));
        XElement association = new(Model + "association", new XAttribute("id", noteId + "_link"),
            new XAttribute("sourceRef", noteId), new XAttribute("targetRef", start.TaskId));
        XElement shape = new(Di + "BPMNShape", new XAttribute("id", "shape_" + noteId), new XAttribute("bpmnElement", noteId),
            Bounds(Left, Left, NoteWidth, plan.NoteHeight));
        XElement edge = new(Di + "BPMNEdge", new XAttribute("id", "edge_" + noteId + "_link"), new XAttribute("bpmnElement", noteId + "_link"),
            new XElement(DdDi + "waypoint", Number("x", Left + 40), Number("y", Left + plan.NoteHeight)),
            new XElement(DdDi + "waypoint", Number("x", start.X + (EventSize / 2)), Number("y", start.ShapeTop)));
        return (note, association, shape, edge);
    }

    /// <summary>The height a note of these lines takes, each wrapped at the note's width.</summary>
    private static double NoteHeight(IEnumerable<string> lines)
    {
        int count = lines.Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)NoteLineLength)));
        return (count * 15) + 16;
    }

    /// <summary>Whether a workflow's diagram is there to embed: its process and its drawing.</summary>
    private static bool Drawable(XDocument? diagram, out XElement? process, out XElement? drawing)
    {
        process = diagram?.Root?.Element(Model + "process");
        drawing = diagram?.Descendants(Di + "BPMNPlane").FirstOrDefault();
        return process is not null && drawing is not null;
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
    /// depth-first walk and left out of the count, or the layering would never end. A flow back then becomes a pair
    /// of link events, a flow that skips layers gets a passing row in each, and every flow runs between neighbours.
    /// </summary>
    private static void Layer(Plan plan)
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
        plan.Return(back);
        foreach (Link link in plan.Links)
        {
            link.Route = [link.From];
            for (int layer = link.From.Layer + 1; layer < link.To.Layer; layer++)
            {
                link.Route.Add(plan.Pass(layer));
            }
            link.Route.Add(link.To);
        }
        plan.Hops.AddRange(plan.Links.SelectMany(link => link.Route.Skip(1).Select((to, index) => new Hop(link, index, link.Route[index], to))));
    }

    /// <summary>
    /// Columns, then rows layer by layer — each unit as near the height its flows arrive at as the units above it
    /// allow — then the heights flows come in at, the order of the upright runs, and the routes.
    /// </summary>
    private static void Place(Plan plan)
    {
        List<Hop> hops = plan.Hops;

        int layers = plan.Units.Max(unit => unit.Layer) + 1;
        double[] widths = new double[layers];
        foreach (Unit unit in plan.Units)
        {
            widths[unit.Layer] = Math.Max(widths[unit.Layer], unit.Width);
        }
        double[] columns = new double[layers];
        for (int layer = 0; layer < layers; layer++)
        {
            columns[layer] = layer == 0 ? Left : columns[layer - 1] + widths[layer - 1] + PreviousGap(hops, layer - 1);
        }

        double top = Left + plan.NoteHeight + 60;
        for (int layer = 0; layer < layers; layer++)
        {
            double cursor = top;
            List<(Unit Unit, double Wanted)> placed = [.. plan.Units.Where(unit => unit.Layer == layer)
                .Select(unit => (unit, Wanted(unit, plan, hops, top)))];
            foreach ((Unit unit, double wanted) in placed.OrderBy(entry => entry.Wanted).ThenBy(entry => entry.Unit.Index))
            {
                unit.X = columns[layer];
                unit.Y = double.IsPositiveInfinity(wanted) ? cursor : Math.Max(cursor, wanted - unit.SpineOffset);
                cursor = unit.Y + unit.Height + RowGap;
            }
        }

        // Flows into one shape come in at heights of their own, in the order they arrive from, so none share a line.
        foreach (IGrouping<Unit, Hop> into in hops.GroupBy(hop => hop.To))
        {
            List<Hop> ordered = [.. into.OrderBy(hop => hop.StartY).ThenBy(hop => hop.Link.Index)];
            Unit target = into.Key;
            for (int index = 0; index < ordered.Count; index++)
            {
                ordered[index].EndY = ordered.Count == 1 || target.Kind == UnitKind.Pass
                    ? target.SpineY
                    : target.ShapeTop + (target.ShapeHeight * (index + 1) / (ordered.Count + 1));
            }
        }

        foreach (IGrouping<int, Hop> gap in hops.GroupBy(hop => hop.From.Layer))
        {
            List<Hop> order = Untangled([.. gap]);
            double start = columns[gap.Key] + widths[gap.Key] + ColumnGap;
            for (int index = 0; index < order.Count; index++)
            {
                order[index].SlotX = start + (index * SlotSpacing) + (SlotSpacing / 2);
            }
        }

        foreach (Link link in plan.Links)
        {
            (List<(double X, double Y)> points, double runY, double runStart, bool below) = Leave(link);
            foreach (Hop hop in hops.Where(hop => hop.Link == link).OrderBy(hop => hop.Index))
            {
                points.Add((hop.SlotX, hop.StartY));
                points.Add((hop.SlotX, hop.EndY));
                points.Add((hop.To.Kind == UnitKind.Pass ? hop.To.X + widths[hop.To.Layer] : hop.To.X, hop.EndY));
            }
            link.Points = [.. points.Where((point, index) => index == 0 || point != points[index - 1])];
            if (link.Outcome is string outcome)
            {
                (double width, double height) = LabelBox.Measure(outcome);
                double y = below ? runY + LabelBox.Gap : runY - LabelBox.Gap - height;
                link.Caption = (runStart + 4, y, width, height);
            }
        }
    }

    private static double PreviousGap(List<Hop> hops, int layer)
    {
        int inGap = hops.Count(hop => hop.From.Layer == layer);
        return ColumnGap + (Math.Max(1, inGap) * SlotSpacing) + GapMargin;
    }

    /// <summary>
    /// The spine a unit would like: level with the flows that come into it, so a chain reads as a straight line and
    /// a diamond's targets stand in the order of its corners. The start lines up with the first stage; a link
    /// event that picks a flow back up has nothing to line up with and goes to the bottom of its layer.
    /// </summary>
    private static double Wanted(Unit unit, Plan plan, List<Hop> hops, double top)
    {
        if (unit.Kind == UnitKind.Start)
        {
            return top + plan.Units[1].SpineOffset;
        }
        List<double> from = [.. hops.Where(hop => hop.To == unit && hop.From.Kind != UnitKind.LinkCatch).Select(hop => hop.StartY)];
        return from.Count == 0 ? double.PositiveInfinity : from.Average();
    }

    /// <summary>
    /// The order of the upright runs in one gap that crosses least. Whether two runs cross depends only on which of
    /// the two stands left, so swapping neighbours while it helps settles where no single swap would.
    /// </summary>
    private static List<Hop> Untangled(List<Hop> hops)
    {
        List<Hop> order = [.. hops.OrderBy(hop => hop.EndY > hop.StartY ? 0 : 1)
            .ThenBy(hop => hop.EndY > hop.StartY ? -hop.StartY : hop.StartY).ThenBy(hop => hop.Link.Index)];
        bool better = true;
        while (better)
        {
            better = false;
            for (int index = 1; index < order.Count; index++)
            {
                if (Crossings(order[index], order[index - 1]) < Crossings(order[index - 1], order[index]))
                {
                    (order[index - 1], order[index]) = (order[index], order[index - 1]);
                    better = true;
                }
            }
        }
        return order;
    }

    /// <summary>
    /// How often two runs in one gap cross with <paramref name="left"/> standing left: the right one's first level
    /// run passes the left one's upright, and the left one's last level run passes the right one's upright.
    /// </summary>
    private static int Crossings(Hop left, Hop right)
    {
        int count = 0;
        if (Between(right.StartY, left.StartY, left.EndY))
        {
            count++;
        }
        if (Between(left.EndY, right.StartY, right.EndY))
        {
            count++;
        }
        return count;
    }

    private static bool Between(double value, double one, double other)
    {
        return value > Math.Min(one, other) && value < Math.Max(one, other);
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
        if (link.Corner == StageOutcome.Failure)
        {
            double y = from.SpineY + from.Drop;
            return ([(centre, from.SpineY + half), (centre, y)], y, centre, true);
        }
        if (link.Corner == StageOutcome.Cancel)
        {
            double y = from.SpineY - from.Drop;
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
        Message,
        LinkThrow,
        LinkCatch,
        Pass,
        End
    }

    /// <summary>
    /// Every workflow box drawn on one map. The first box for a workflow carries its diagram; every later one is a
    /// call activity saying where that first one is.
    /// </summary>
    private sealed class Boxes(Func<Guid, XDocument?> workflowDiagram, IReadOnlyDictionary<Guid, string> workflowFiles)
    {
        public Dictionary<Guid, FirstBox> First { get; } = [];

        public List<XElement> Drilldowns { get; } = [];

        public int Count { get; private set; }

        public int Missing { get; private set; }

        public XElement Box(string id, string name, Guid workflow, string firedBy, out bool embedded)
        {
            Count++;
            string file = workflowFiles.TryGetValue(workflow, out string? path) ? $" Kendi diyagramı: {path}.bpmn." : "";
            if (First.TryGetValue(workflow, out FirstBox? first))
            {
                first.Count++;
                embedded = false;
                string where = first.Missing
                    ? "bu çalıştırmada bulunamadı"
                    : $"adımları haritada ilk geçtiği kutunun içinde ({first.FiredBy})";
                return new XElement(Model + "callActivity", new XAttribute("id", id), new XAttribute("name", name),
                    new XElement(Model + "documentation",
                        $"İş akışı: {name} ({workflow:D}). Çalıştıran: {firedBy}. Aynı iş akışı bu haritada daha önce de geçiyor; {where}.{file}"));
            }
            First[workflow] = new FirstBox(name, firedBy, First.Count);
            XElement subProcess = new(Model + "subProcess", new XAttribute("id", id), new XAttribute("name", name));
            if (!Drawable(workflowDiagram(workflow), out XElement? source, out XElement? drawing))
            {
                First[workflow].Missing = true;
                Missing++;
                embedded = false;
                subProcess.Add(new XElement(Model + "documentation",
                    $"İş akışı {name} ({workflow:D}) bu çalıştırmada bulunamadı: silinmiş, taslak ya da dışa aktarımın dışında kalmış olabilir. Çalıştıran: {firedBy}."));
                return subProcess;
            }

            embedded = true;
            string prefix = id + "_";
            subProcess.Add(new XElement(Model + "documentation", $"İş akışı: {name} ({workflow:D}). Çalıştıran: {firedBy}.{file}"));
            foreach (XElement child in source!.Elements().Where(child => child.Name != Model + "documentation" && child.Name != Model + "extensionElements"))
            {
                subProcess.Add(Prefixed(child, prefix));
            }
            XElement innerPlane = new(Di + "BPMNPlane", new XAttribute("id", "plane_" + id), new XAttribute("bpmnElement", id),
                drawing!.Elements().Select(child => Prefixed(child, prefix)));
            Drilldowns.Add(new XElement(Di + "BPMNDiagram", new XAttribute("id", "diagram_" + id), innerPlane));
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
    }

    /// <summary>A workflow's first box on a map: where it is, and how many boxes it took in all.</summary>
    private sealed class FirstBox(string name, string firedBy, int order)
    {
        public string Name { get; } = name;

        public string FiredBy { get; } = firedBy;

        public int Order { get; } = order;

        public int Count { get; set; } = 1;

        public bool Missing { get; set; }
    }

    /// <summary>
    /// One thing laid out as a block: the start, a stage (its task, the workflow it always fires, and — when it has
    /// more than one way out — its diamond), a workflow, a message, a link event, an end, or the row a flow passes a
    /// layer on. A unit's box includes the room its captions, labels and corner runs need, so units stacked in a
    /// column cannot reach each other.
    /// </summary>
    private sealed class Unit(int index, UnitKind kind, string id, string name)
    {
        public int Index { get; } = index;

        public UnitKind Kind { get; } = kind;

        public string TaskId { get; } = id;

        public string Name { get; set; } = name;

        public CaseStage? Stage { get; init; }

        public Guid? WorkflowId { get; init; }

        /// <summary>The stage a workflow box is fired from, and on which outcomes.</summary>
        public (Unit Stage, string Outcome)? Source { get; init; }

        /// <summary>Read when drawn, after two stages of one name have been told apart.</summary>
        public string FiredBy
        {
            get { return Source is (Unit stage, string outcome) ? $"{stage.Name} — {outcome}" : ""; }
        }

        public string? Documentation { get; init; }

        public string? LinkName { get; init; }

        /// <summary>A workflow the stage fires whichever way it closes, drawn between the task and the diamond.</summary>
        public (Guid Workflow, string Name)? Always { get; set; }

        public bool Embedded { get; set; }

        public bool HasGateway { get; set; }

        public int Layer { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public bool IsEvent
        {
            get { return Kind is UnitKind.Start or UnitKind.End or UnitKind.Message or UnitKind.LinkThrow or UnitKind.LinkCatch; }
        }

        public string GatewayId
        {
            get { return TaskId + "_karar"; }
        }

        public string AlwaysId
        {
            get { return TaskId + "_wf_" + Always!.Value.Workflow.ToString("N"); }
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
            get
            {
                if (Kind == UnitKind.Pass)
                {
                    return 0;
                }
                return IsEvent ? EventSize : FlowNode.Size(FlowNodeType.ServiceTask, Name).Width;
            }
        }

        public double ShapeHeight
        {
            get
            {
                if (Kind == UnitKind.Pass)
                {
                    return 0;
                }
                return IsEvent ? EventSize : FlowNode.Size(FlowNodeType.ServiceTask, Name).Height;
            }
        }

        public double AlwaysWidth
        {
            get { return Always is (Guid, string name) ? FlowNode.Size(FlowNodeType.ServiceTask, name).Width : 0; }
        }

        private double AlwaysHeight
        {
            get { return Always is (Guid, string name) ? FlowNode.Size(FlowNodeType.ServiceTask, name).Height : 0; }
        }

        public double Width
        {
            get
            {
                double always = Always is null ? 0 : TaskToGateway + AlwaysWidth;
                return ShapeWidth + always + (HasGateway ? TaskToGateway + GatewaySize : 0);
            }
        }

        /// <summary>The height of the caption on each corner of the diamond, by the outcome whose corner it is.</summary>
        public Dictionary<string, double> Captions { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// How far the top and bottom corners' flows go before they turn: far enough that the right corner's caption,
        /// drawn above the spine, ends before the top corner's run — a caption that wraps to two lines reached it.
        /// </summary>
        public double Drop
        {
            get { return Math.Max(CornerDrop, LabelBox.Gap + Captions.GetValueOrDefault(StageOutcome.Success) + LabelBox.Gap); }
        }

        /// <summary>Above and below the tallest shape: the corner run and the caption beyond it.</summary>
        private double Margin
        {
            get
            {
                double caption = Math.Max(Captions.GetValueOrDefault(StageOutcome.Cancel), Captions.GetValueOrDefault(StageOutcome.Failure));
                return Math.Max(CellMargin, Drop + LabelBox.Gap + caption - (Tallest / 2));
            }
        }

        /// <summary>The tallest shape on the spine: the task, the workflow it always fires, the diamond.</summary>
        private double Tallest
        {
            get { return Math.Max(Math.Max(ShapeHeight, AlwaysHeight), HasGateway ? GatewaySize : 0); }
        }

        /// <summary>The shape, its text below it where the text is drawn outside, and room for corner runs.</summary>
        public double Height
        {
            get
            {
                if (IsEvent)
                {
                    return EventSize + LabelBox.Gap + LabelBox.Measure(Name).Height;
                }
                return HasGateway ? Tallest + (Margin * 2) : Tallest;
            }
        }

        /// <summary>How far below the unit's top its flows come in and go out.</summary>
        public double SpineOffset
        {
            get
            {
                if (IsEvent)
                {
                    return EventSize / 2;
                }
                return HasGateway ? Margin + (Tallest / 2) : Tallest / 2;
            }
        }

        public double SpineY
        {
            get { return Y + SpineOffset; }
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

        public double AlwaysX
        {
            get { return TaskRight + TaskToGateway; }
        }

        public double GatewayX
        {
            get { return Always is null ? TaskRight + TaskToGateway : AlwaysX + AlwaysWidth + TaskToGateway; }
        }
    }

    /// <summary>One flow of the map: an outcome, or the step from a workflow or message to what follows it.</summary>
    private sealed class Link(int index, Unit from, Unit to, string? outcome, string? corner)
    {
        public int Index { get; } = index;

        public string Id { get; } = "f" + index.ToString(CultureInfo.InvariantCulture);

        public Unit From { get; } = from;

        public Unit To { get; set; } = to;

        /// <summary>The outcomes this flow stands for, as its caption reads.</summary>
        public string? Outcome { get; } = outcome;

        /// <summary>The outcome whose corner of the diamond the flow leaves from: the first of those it stands for.</summary>
        public string? Corner { get; } = corner;

        public List<Unit> Route { get; set; } = [];

        public IReadOnlyList<(double X, double Y)> Points { get; set; } = [];

        public (double X, double Y, double Width, double Height)? Caption { get; set; }
    }

    /// <summary>One leg of a flow, from one layer to the next, with its upright run in the gap between them.</summary>
    private sealed class Hop(Link link, int index, Unit from, Unit to)
    {
        public Link Link { get; } = link;

        public int Index { get; } = index;

        public Unit From { get; } = from;

        public Unit To { get; } = to;

        public double StartY
        {
            get
            {
                if (Index > 0)
                {
                    return From.SpineY;
                }
                if (!From.HasGateway || Link.Corner == StageOutcome.Success)
                {
                    return From.SpineY;
                }
                return Link.Corner == StageOutcome.Failure ? From.SpineY + From.Drop : From.SpineY - From.Drop;
            }
        }

        public double EndY { get; set; }

        public double SlotX { get; set; }
    }

    /// <summary>The units and flows of one map, found by walking from the primary stage in the order a case would.</summary>
    private sealed class Plan
    {
        private readonly Dictionary<Unit, Unit> _catches = [];

        public List<Unit> Units { get; } = [];

        public List<Link> Links { get; } = [];

        public List<Hop> Hops { get; } = [];

        public double NoteHeight { get; set; }

        public static Plan From(CaseStage primary, IReadOnlyDictionary<Guid, CaseStage> stages)
        {
            Plan plan = new();
            Unit start = plan.Add(UnitKind.Start, "baslangic", "Talep açılır");
            Dictionary<Guid, Unit> cells = [];
            Queue<CaseStage> queue = new();
            plan.Link(start, plan.Cell(primary, primary, cells, queue), null, null);
            while (queue.TryDequeue(out CaseStage? stage))
            {
                plan.Exits(stage, cells[stage.Id], primary, stages, cells, queue);
            }
            plan.Disambiguate();
            return plan;
        }

        /// <summary>
        /// A stage's ways out, one per group of outcomes that agree — same next stage, same workflow, same message —
        /// so a diamond is drawn only where the outcomes really go different ways.
        /// </summary>
        private void Exits(CaseStage stage, Unit cell, CaseStage primary, IReadOnlyDictionary<Guid, CaseStage> stages,
            Dictionary<Guid, Unit> cells, Queue<CaseStage> queue)
        {
            List<List<StageExit>> groups = [.. stage.Exits
                .Where(exit => exit.NextStage is not null || exit.Workflow is not null || exit.Sms is not null)
                .OrderBy(exit => Array.IndexOf(Outcomes, exit.Outcome))
                .GroupBy(exit => (exit.NextStage, exit.Workflow, exit.Sms))
                .Select(group => group.ToList())];
            if (groups.Count == 0)
            {
                Link(cell, Add(UnitKind.End, cell.TaskId + "_son", "Aşamanın çıkışı tanımlı değil", documentation: NoNext), null, null);
                return;
            }
            bool always = groups.Count > 1 && groups.All(group => group[0].Workflow is not null && group[0].Workflow == groups[0][0].Workflow);
            if (always)
            {
                cell.Always = (groups[0][0].Workflow!.Value, groups[0][0].WorkflowName ?? groups[0][0].Workflow!.Value.ToString("D"));
            }
            cell.HasGateway = groups.Count > 1;
            foreach (List<StageExit> group in groups)
            {
                StageExit exit = group[0];
                string outcome = string.Join(" / ", group.Select(member => member.Outcome));
                string part = cell.TaskId + "_" + string.Join("-", group.Select(member => member.Outcome));
                Unit next = exit.NextStage is Guid id
                    ? stages.TryGetValue(id, out CaseStage? found)
                        ? Cell(found, primary, cells, queue)
                        : Add(UnitKind.End, part + "_yok", "Aşama bulunamadı: " + (exit.NextStageName ?? id.ToString("D")))
                    : Add(UnitKind.End, part + "_son", "Sonraki aşama yok", documentation: NoNext);
                if (exit.Workflow is Guid workflow && !always)
                {
                    Unit fired = Add(UnitKind.Workflow, part + "_wf_" + workflow.ToString("N"), exit.WorkflowName ?? workflow.ToString("D"),
                        workflow: workflow, source: (cell, outcome));
                    Link(fired, next, null, null);
                    next = fired;
                }
                if (exit.Sms is string sms)
                {
                    Unit message = Add(UnitKind.Message, part + "_sms", "SMS gönderilir", documentation: sms);
                    Link(message, next, null, null);
                    next = message;
                }
                cell.Captions[exit.Outcome] = LabelBox.HeightOf(outcome);
                Link(cell, next, outcome, exit.Outcome);
            }
        }

        private const string NoNext = "Aşama kaydı bu sonuç için bir sonraki aşama göstermiyor; talebin kapanıp kapanmadığı aşamanın kapanış kurallarına bağlıdır.";

        /// <summary>
        /// Every flow found going back becomes a pair of link events: the flow ends at "… aşamasına dön" beside where
        /// it starts, and one "… aşamasına dönüş" per stage returned to leads into that stage from beside it.
        /// </summary>
        public void Return(HashSet<Link> back)
        {
            List<Link> returning = [.. Links.Where(back.Contains)];
            foreach (Link link in returning)
            {
                Unit target = link.To;
                string part = link.Outcome is null ? "" : "_" + link.Outcome.Replace(" / ", "-", StringComparison.Ordinal);
                Unit thrown = Add(UnitKind.LinkThrow, link.From.TaskId + part + "_don", $"\"{target.Name}\" aşamasına dön", linkName: target.Name);
                thrown.Layer = link.From.Layer + 1;
                link.To = thrown;
                if (!_catches.ContainsKey(target))
                {
                    Unit caught = Add(UnitKind.LinkCatch, target.TaskId + "_donus", $"\"{target.Name}\" aşamasına dönüş", linkName: target.Name);
                    caught.Layer = target.Layer - 1;
                    _catches[target] = caught;
                    Link(caught, target, null, null);
                }
            }
        }

        public Unit Pass(int layer)
        {
            Unit pass = Add(UnitKind.Pass, "gecis_" + Units.Count.ToString(CultureInfo.InvariantCulture), "");
            pass.Layer = layer;
            return pass;
        }

        /// <summary>
        /// Two stages a reader cannot tell apart on the map — the same name, as different records — are told apart:
        /// by their step code where each has its own, otherwise by number in the order a case meets them.
        /// </summary>
        private void Disambiguate()
        {
            foreach (IGrouping<string, Unit> same in Units.Where(unit => unit.Stage is not null)
                .GroupBy(unit => TurkishFold.Fold(unit.Name), StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                List<Unit> units = [.. same];
                List<string?> codes = [.. units.Select(unit => unit.Stage!.Fields.GetValueOrDefault("ps_stepcode"))];
                bool byCode = codes.All(code => !string.IsNullOrWhiteSpace(code)) && codes.Distinct(StringComparer.Ordinal).Count() == codes.Count;
                for (int index = 0; index < units.Count; index++)
                {
                    units[index].Name += byCode ? " · " + codes[index] : $" ({index + 1})";
                }
            }
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
            Unit cell = Add(UnitKind.Cell, "asama_" + stage.Id.ToString("N"), name, stage);
            cells[stage.Id] = cell;
            queue.Enqueue(stage);
            return cell;
        }

        private Unit Add(UnitKind kind, string id, string name, CaseStage? stage = null, Guid? workflow = null,
            (Unit, string)? source = null, string? documentation = null, string? linkName = null)
        {
            Unit unit = new(Units.Count, kind, id, name)
            {
                Stage = stage,
                WorkflowId = workflow,
                Source = source,
                Documentation = documentation,
                LinkName = linkName
            };
            Units.Add(unit);
            return unit;
        }

        private void Link(Unit from, Unit to, string? outcome, string? corner)
        {
            Links.Add(new Link(Links.Count, from, to, outcome, corner));
        }
    }
}
