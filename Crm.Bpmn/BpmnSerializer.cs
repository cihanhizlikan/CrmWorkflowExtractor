using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;
using Crm.Ir.Text;

namespace Crm.Bpmn;

/// <summary>Flow graph → BPMN 2.0 XML with diagram interchange (§6.2). Element and attribute order is fixed, so output is byte-stable.</summary>
public static class BpmnSerializer
{
    public static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    public static readonly XNamespace Di = "http://www.omg.org/spec/BPMN/20100524/DI";
    public static readonly XNamespace Dc = "http://www.omg.org/spec/DD/20100524/DC";
    public static readonly XNamespace DdDi = "http://www.omg.org/spec/DD/20100524/DI";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>The machine-readable provenance namespace (§6.2): which CRM workflow and step each element came from.</summary>
    public static readonly XNamespace Provenance = "urn:crm-workflow-extractor:provenance:1";

    public static XDocument ToXml(BpmnProcess process, string toolVersion)
    {
        XElement processElement = new(Model + "process",
            new XAttribute("id", process.ProcessId),
            new XAttribute("name", process.Name),
            new XAttribute("isExecutable", "false"),
            Documentation(process.Documentation),
            Extensions(process.Sources, null));
        foreach (FlowNode node in process.Graph.Nodes)
        {
            processElement.Add(Node(node));
        }
        foreach (FlowEdge edge in process.Graph.Edges)
        {
            processElement.Add(Flow(edge));
        }
        // Artifacts follow every flow element, as the schema's sequence requires.
        if (process.Note is not null && process.Graph.Nodes.Count > 0)
        {
            processElement.Add(new XElement(Model + "textAnnotation", new XAttribute("id", NoteId(process)),
                new XElement(Model + "text", process.Note)));
            processElement.Add(new XElement(Model + "association", new XAttribute("id", NoteId(process) + "_link"),
                new XAttribute("sourceRef", NoteId(process)), new XAttribute("targetRef", process.Graph.Nodes[0].Id)));
        }

        XElement plane = new(Di + "BPMNPlane", new XAttribute("id", "plane_" + process.ProcessId), new XAttribute("bpmnElement", process.ProcessId));
        foreach (FlowNode node in process.Graph.Nodes)
        {
            XElement shape = new(Di + "BPMNShape", new XAttribute("id", "shape_" + node.Id), new XAttribute("bpmnElement", node.Id));
            if (node.Type == FlowNodeType.ExclusiveGateway)
            {
                shape.Add(new XAttribute("isMarkerVisible", "true"));
            }
            shape.Add(new XElement(Dc + "Bounds", Number("x", node.X), Number("y", node.Y), Number("width", node.Width), Number("height", node.Height)));
            if (ShapeLabel(node) is XElement shapeLabel)
            {
                shape.Add(shapeLabel);
            }
            plane.Add(shape);
        }
        foreach (FlowEdge edge in process.Graph.Edges)
        {
            XElement shape = new(Di + "BPMNEdge", new XAttribute("id", "edge_" + edge.Id), new XAttribute("bpmnElement", edge.Id));
            IReadOnlyList<(double X, double Y)> waypoints = Waypoints(process.Graph.Node(edge.SourceId), process.Graph.Node(edge.TargetId));
            foreach ((double x, double y) in waypoints)
            {
                shape.Add(new XElement(DdDi + "waypoint", Number("x", x), Number("y", y)));
            }
            if (EdgeLabel(edge, waypoints) is XElement edgeLabel)
            {
                shape.Add(edgeLabel);
            }
            plane.Add(shape);
        }

        if (process.Note is not null && process.Graph.Nodes.Count > 0)
        {
            (double x, double y, double width, double height) = process.NoteBounds;
            plane.Add(new XElement(Di + "BPMNShape", new XAttribute("id", "shape_" + NoteId(process)), new XAttribute("bpmnElement", NoteId(process)),
                new XElement(Dc + "Bounds", Number("x", x), Number("y", y), Number("width", width), Number("height", height))));
            FlowNode first = process.Graph.Nodes[0];
            plane.Add(new XElement(Di + "BPMNEdge", new XAttribute("id", "edge_" + NoteId(process) + "_link"),
                new XAttribute("bpmnElement", NoteId(process) + "_link"),
                new XElement(DdDi + "waypoint", Number("x", x + 40), Number("y", y + height)),
                new XElement(DdDi + "waypoint", Number("x", first.X + (first.Width / 2)), Number("y", first.Y))));
        }

        XElement definitions = new(Model + "definitions",
            // Declared explicitly: xsi:type="tFormalExpression" is a QName and resolves against the default namespace.
            new XAttribute("xmlns", Model.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "bpmndi", Di),
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(XNamespace.Xmlns + "di", DdDi),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(XNamespace.Xmlns + "cwe", Provenance),
            new XAttribute("id", "def_" + process.ProcessId),
            new XAttribute("targetNamespace", "urn:crm-workflow-extractor:processes"),
            new XAttribute("exporter", "CrmWorkflowExtractor"),
            new XAttribute("exporterVersion", toolVersion),
            processElement,
            new XElement(Di + "BPMNDiagram", new XAttribute("id", "diagram_" + process.ProcessId), plane));
        return Writable(new XDocument(new XDeclaration("1.0", "UTF-8", null), definitions));
    }

    /// <summary>
    /// The finished document with every character XML cannot carry taken out of it.
    ///
    /// <para>
    /// A workflow's name arrives as JSON and a plug-in's strings arrive as bytes, so either can hold a control
    /// character; <see cref="XmlWriter"/> has no spelling for one and THROWS rather than writing a bad file. The
    /// loop that emits the diagrams does not guard itself, and it runs before the reports, so one stray byte in
    /// one name used to cost the whole run — every workbook with it. It is swept here, over the built document,
    /// rather than at each of the dozen places text goes in, because that is the version a later site cannot
    /// forget to call.
    /// </para>
    /// </summary>
    private static XDocument Writable(XDocument document)
    {
        foreach (XElement element in document.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                string clean = DocumentText.Writable(attribute.Value);
                if (!ReferenceEquals(clean, attribute.Value))
                {
                    attribute.Value = clean;
                }
            }
        }
        foreach (XText text in document.DescendantNodes().OfType<XText>())
        {
            string clean = DocumentText.Writable(text.Value);
            if (!ReferenceEquals(clean, text.Value))
            {
                text.Value = clean;
            }
        }
        return document;
    }

    /// <summary>UTF-8 without BOM, LF line endings, two-space indent.</summary>
    public static byte[] ToBytes(XDocument document)
    {
        XmlWriterSettings settings = new()
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using MemoryStream stream = new();
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string NoteId(BpmnProcess process)
    {
        return "note_" + process.ProcessId;
    }

    private static XElement Node(FlowNode node)
    {
        string element = node.Type switch
        {
            FlowNodeType.StartEvent => "startEvent",
            FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent => "endEvent",
            FlowNodeType.ServiceTask => "serviceTask",
            FlowNodeType.SendTask => "sendTask",
            FlowNodeType.UserTask => "userTask",
            FlowNodeType.BusinessRuleTask => "businessRuleTask",
            FlowNodeType.CallActivity => "callActivity",
            FlowNodeType.ExclusiveGateway => "exclusiveGateway",
            FlowNodeType.EventBasedGateway => "eventBasedGateway",
            FlowNodeType.ConditionalCatchEvent or FlowNodeType.TimerCatchEvent => "intermediateCatchEvent",
            _ => "task"
        };
        XElement xml = new(Model + element, new XAttribute("id", node.Id));
        if (node.Name.Length > 0)
        {
            xml.Add(new XAttribute("name", node.Name));
        }
        if (node.Type == FlowNodeType.CallActivity && node.Expression is not null)
        {
            xml.Add(new XAttribute("calledElement", node.Expression));
        }
        if (node.DefaultFlow is not null)
        {
            xml.Add(new XAttribute("default", node.DefaultFlow));
        }
        if (node.Documentation is not null)
        {
            xml.Add(Documentation(node.Documentation));
        }
        if (node.Sources.Count > 0 || node.ActivityType is not null)
        {
            xml.Add(Extensions(node.Sources, node.ActivityType));
        }

        // Event definitions follow documentation and extensionElements, as the schema's sequence requires.
        if (node.Type == FlowNodeType.TerminateEndEvent)
        {
            xml.Add(new XElement(Model + "terminateEventDefinition", new XAttribute("id", node.Id + "_terminate")));
        }
        else if (node.Type == FlowNodeType.ConditionalCatchEvent || (node.Type == FlowNodeType.StartEvent && node.Expression is not null))
        {
            xml.Add(new XElement(Model + "conditionalEventDefinition", new XAttribute("id", node.Id + "_condition"),
                Expression("condition", node.Expression ?? "")));
        }
        else if (node.Type == FlowNodeType.TimerCatchEvent)
        {
            XElement timer = new(Model + "timerEventDefinition", new XAttribute("id", node.Id + "_timer"));
            if (node.Expression is not null)
            {
                timer.Add(Expression("timeDate", node.Expression));
            }
            xml.Add(timer);
        }
        return xml;
    }

    private static XElement Flow(FlowEdge edge)
    {
        XElement xml = new(Model + "sequenceFlow", new XAttribute("id", edge.Id));
        if (!string.IsNullOrEmpty(edge.Name))
        {
            xml.Add(new XAttribute("name", edge.Name));
        }
        xml.Add(new XAttribute("sourceRef", edge.SourceId), new XAttribute("targetRef", edge.TargetId));
        if (edge.Condition is not null)
        {
            xml.Add(Expression("conditionExpression", edge.Condition));
        }
        return xml;
    }

    private static XElement Documentation(string text)
    {
        return new XElement(Model + "documentation", text);
    }

    private static XElement Extensions(IReadOnlyList<StepSource> sources, string? activityType)
    {
        XElement extensions = new(Model + "extensionElements");
        foreach (StepSource source in sources)
        {
            extensions.Add(new XElement(Provenance + "source",
                new XAttribute("workflowId", source.WorkflowId.ToString("D")),
                new XAttribute("stepPath", source.Path)));
        }
        if (activityType is not null)
        {
            extensions.Add(new XElement(Provenance + "customActivity", new XAttribute("type", activityType)));
        }
        return extensions;
    }

    private static XElement Expression(string name, string text)
    {
        return new XElement(Model + name, new XAttribute(Xsi + "type", "tFormalExpression"), text);
    }

    private static XAttribute Number(string name, double value)
    {
        return new XAttribute(name, Math.Round(value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Right-centre of the source to left-centre of the target, with an orthogonal elbow when they are not level.</summary>
    /// <summary>
    /// Where a shape's text goes. A task is a box and holds its own text, but a gateway is a 50-pixel diamond and an
    /// event a 36-pixel circle: without these bounds a viewer paints the text across the shape, which is unreadable.
    /// The label sits centred just below the shape, in the room the layout leaves between rows.
    /// </summary>
    private static XElement? ShapeLabel(FlowNode node)
    {
        if (node.Name.Length == 0 || node.Type is not (FlowNodeType.ExclusiveGateway or FlowNodeType.EventBasedGateway
            or FlowNodeType.StartEvent or FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent
            or FlowNodeType.ConditionalCatchEvent or FlowNodeType.TimerCatchEvent))
        {
            return null;
        }
        (double width, double height) = LabelSize(node.Name);
        double x = node.X + (node.Width / 2) - (width / 2);
        return Label(x, node.Y + node.Height + 6, width, height);
    }

    /// <summary>
    /// A branch caption, placed on the FLOW it belongs to: at the middle of the line, just clear of it.
    ///
    /// <para>
    /// It used to be placed against the TARGET shape — centred over it, above it — which put "evet" past the
    /// arrowhead and floating over the task, and "hayır" at the far end of a flow three hundred pixels long,
    /// beside the diamond it arrived at rather than the one it left. A caption belongs to its line, so it is
    /// measured along the line: halfway by length, above a level run and beside an upright one.
    /// </para>
    /// </summary>
    private static XElement? EdgeLabel(FlowEdge edge, IReadOnlyList<(double X, double Y)> waypoints)
    {
        if (edge.Name is not string name || name.Length == 0 || waypoints.Count < 2)
        {
            return null;
        }
        (double width, double height) = LabelSize(name);
        (double x, double y, bool upright) = Middle(waypoints);
        if (upright)
        {
            return Label(x + LabelGap, y - (height / 2), width, height);
        }
        return Label(x - (width / 2), y - LabelGap - height, width, height);
    }

    /// <summary>Clear of the line by this much, so the caption reads as beside it rather than written through it.</summary>
    private const double LabelGap = 6;

    /// <summary>
    /// The point halfway ALONG a flow, measured by length rather than by counting corners — a flow that leaves a
    /// gateway sideways and then runs level is two segments of very different sizes, and its middle is the point
    /// a reader's eye follows, not the bend. Also reports whether the line is upright there, which decides
    /// whether the caption sits above it or beside it.
    /// </summary>
    private static (double X, double Y, bool Upright) Middle(IReadOnlyList<(double X, double Y)> waypoints)
    {
        double total = 0;
        for (int index = 1; index < waypoints.Count; index++)
        {
            total += Length(waypoints[index - 1], waypoints[index]);
        }
        double remaining = total / 2;
        for (int index = 1; index < waypoints.Count; index++)
        {
            (double X, double Y) from = waypoints[index - 1];
            (double X, double Y) to = waypoints[index];
            double length = Length(from, to);
            if (length <= 0)
            {
                continue;
            }
            if (remaining <= length)
            {
                double ratio = remaining / length;
                return (from.X + ((to.X - from.X) * ratio), from.Y + ((to.Y - from.Y) * ratio),
                    Math.Abs(to.X - from.X) < 0.5);
            }
            remaining -= length;
        }
        return (waypoints[^1].X, waypoints[^1].Y, false);
    }

    private static double Length((double X, double Y) from, (double X, double Y) to)
    {
        return Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
    }

    private static XElement Label(double x, double y, double width, double height)
    {
        return new XElement(Di + "BPMNLabel", new XElement(Dc + "Bounds", Number("x", x), Number("y", y), Number("width", width), Number("height", height)));
    }

    /// <summary>
    /// Room for the text as a viewer actually draws it. Viewers wrap an external label at their own fixed width
    /// (90 pixels in bpmn.io) and centre it on these bounds, growing up and down, so the height is what matters:
    /// too little and the text spills over the shape below. About 6.2 pixels a character at the 11-pixel font.
    /// </summary>
    private static (double Width, double Height) LabelSize(string text)
    {
        const double wrapWidth = 90;
        // Twelve lines rather than five: a condition is never shortened, so its box has to be able to hold one.
        // Too few and the viewer draws the text outside the bounds it was given, over whatever is below.
        double lines = Math.Min(12, Math.Ceiling(((text.Length * 6.2) + 4) / wrapWidth));
        return (wrapWidth, Math.Round((lines * 13) + 6));
    }

    private static IReadOnlyList<(double X, double Y)> Waypoints(FlowNode source, FlowNode target)
    {
        double sx = source.X + source.Width;
        double sy = source.Y + (source.Height / 2);
        double tx = target.X;
        double ty = target.Y + (target.Height / 2);
        if (Math.Abs(sy - ty) < 0.5)
        {
            return [(sx, sy), (tx, ty)];
        }
        if (source.Type is FlowNodeType.ExclusiveGateway or FlowNodeType.EventBasedGateway)
        {
            // Leave a split from its top or bottom corner, then run level into the branch.
            double gatewayX = source.X + (source.Width / 2);
            double gatewayY = ty < sy ? source.Y : source.Y + source.Height;
            return [(gatewayX, gatewayY), (gatewayX, ty), (tx, ty)];
        }
        if (target.Type == FlowNodeType.ExclusiveGateway)
        {
            double joinX = target.X + (target.Width / 2);
            double joinY = sy < ty ? target.Y : target.Y + target.Height;
            return [(sx, sy), (joinX, sy), (joinX, joinY)];
        }
        double middle = (sx + tx) / 2;
        return [(sx, sy), (middle, sy), (middle, ty), (tx, ty)];
    }
}
