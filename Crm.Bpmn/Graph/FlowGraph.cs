using Crm.Ir.Model;

namespace Crm.Bpmn.Graph;

/// <summary>The BPMN element a flow node becomes.</summary>
public enum FlowNodeType
{
    StartEvent,
    EndEvent,
    TerminateEndEvent,
    ServiceTask,
    SendTask,
    UserTask,
    BusinessRuleTask,
    CallActivity,
    Task,
    ExclusiveGateway,
    EventBasedGateway,
    ConditionalCatchEvent,
    TimerCatchEvent
}

/// <summary>A node before serialization: identity, BPMN type, text, provenance and — after layout — its bounds.</summary>
public sealed class FlowNode(string id, FlowNodeType type, string name)
{
    public string Id { get; } = id;

    public FlowNodeType Type { get; } = type;

    public string Name { get; } = name;

    public string? Documentation { get; init; }

    /// <summary>For conditional events: the condition; for timers: the time expression; for call activities: the called process id.</summary>
    public string? Expression { get; init; }

    /// <summary>For an exclusive split: the id of the flow taken when no condition holds.</summary>
    public string? DefaultFlow { get; set; }

    /// <summary>
    /// The caption owed to a default flow that does not exist yet. A split whose only continuing path is the
    /// bypass needs no join gateway: the bypass IS the flow onwards, and that flow is made by the sequence around
    /// the split, after the split itself was built. The first flow out of the split from then on takes this.
    /// </summary>
    public string? OwedDefaultLabel { get; set; }

    public IReadOnlyList<StepSource> Sources { get; init; } = [];

    /// <summary>For a custom activity: its assembly-qualified type, carried in the extension block.</summary>
    public string? ActivityType { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width
    {
        get { return Size(Type, Name).Width; }
    }

    public double Height
    {
        get { return Size(Type, Name).Height; }
    }

    public bool IsEnd
    {
        get { return Type is FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent; }
    }

    /// <summary>Room for a character of the label a viewer draws INSIDE a task, at the 12-pixel font it uses.</summary>
    private const double Character = 6.6;

    /// <summary>Padding inside a task box: the text is not drawn against its border.</summary>
    private const double Padding = 10;

    /// <summary>
    /// How big a shape is drawn. An event or a gateway is a fixed glyph whose caption hangs OUTSIDE it, so its
    /// size says nothing about its text. A task carries its text INSIDE, and the text is no longer shortened to
    /// fit — the fields a step writes are the substance of it — so the box is grown to hold what it says: wider
    /// first, up to a width that still reads as a box rather than a banner, and taller after that.
    /// </summary>
    public static (double Width, double Height) Size(FlowNodeType type, string name)
    {
        if (type is FlowNodeType.StartEvent or FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent
            or FlowNodeType.ConditionalCatchEvent or FlowNodeType.TimerCatchEvent)
        {
            return (36, 36);
        }
        if (type is FlowNodeType.ExclusiveGateway or FlowNodeType.EventBasedGateway)
        {
            return (50, 50);
        }
        const double narrowest = 130;
        const double widest = 300;
        const double shortest = 70;
        double wanted = (name.Length * Character) + (Padding * 2);
        double width = Math.Clamp(Math.Ceiling(wanted / 10) * 10, narrowest, widest);
        double lines = Math.Max(1, Math.Ceiling((name.Length * Character) / (width - (Padding * 2))));
        return (width, Math.Max(shortest, Math.Ceiling(((lines * 15) + (Padding * 2)) / 10) * 10));
    }
}

public sealed record FlowEdge(string Id, string SourceId, string TargetId, string? Name, string? Condition);

/// <summary>Nodes and edges of one BPMN process, in insertion order — which is deterministic, so output is too.</summary>
public sealed class FlowGraph
{
    private readonly Dictionary<string, FlowNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<FlowNode> _order = [];
    private readonly List<FlowEdge> _edges = [];

    public IReadOnlyList<FlowNode> Nodes
    {
        get { return _order; }
    }

    public IReadOnlyList<FlowEdge> Edges
    {
        get { return _edges; }
    }

    public FlowNode Add(FlowNode node)
    {
        if (!_nodes.TryAdd(node.Id, node))
        {
            throw new InvalidOperationException($"Duplicate BPMN element id '{node.Id}'.");
        }
        _order.Add(node);
        return node;
    }

    public FlowNode Node(string id)
    {
        return _nodes[id];
    }

    /// <summary>
    /// Folds a merge gateway into the merge it feeds. A condition nested inside another produces two of them back
    /// to back — the inner one gathering its own branches, the outer gathering those plus its own — joined by a
    /// single arrow. Along the path a reader follows, the second reads as a diamond with one arrow in and one
    /// out: a decision shape that decides nothing, which has to be read before that is clear. Merging them loses
    /// no path, because everything arriving at the first was already going on to the second.
    ///
    /// <para>
    /// Only UNNAMED gateways joined by a bare flow are folded: a named one is a decision, and a flow carrying a
    /// caption or a condition is carrying something a reader needs.
    /// </para>
    /// </summary>
    public void FoldMergeGateways()
    {
        while (Fold())
        {
        }
    }

    private bool Fold()
    {
        foreach (FlowNode node in _order)
        {
            if (node.Type != FlowNodeType.ExclusiveGateway || node.Name.Length > 0)
            {
                continue;
            }
            List<FlowEdge> leaving = [.. _edges.Where(edge => edge.SourceId == node.Id)];
            if (leaving.Count != 1 || leaving[0].Name is not null || leaving[0].Condition is not null)
            {
                continue;
            }
            if (!_nodes.TryGetValue(leaving[0].TargetId, out FlowNode? next)
                || next.Type != FlowNodeType.ExclusiveGateway || next.Name.Length > 0 || next.Id == node.Id)
            {
                continue;
            }
            for (int index = 0; index < _edges.Count; index++)
            {
                if (_edges[index].TargetId == node.Id)
                {
                    // Retargeted rather than rebuilt: the flow keeps its id, so a split still points its default at it.
                    _edges[index] = _edges[index] with { TargetId = next.Id };
                }
            }
            _edges.Remove(leaving[0]);
            _nodes.Remove(node.Id);
            _order.Remove(node);
            return true;
        }
        return false;
    }

    public FlowEdge Connect(string sourceId, string targetId, string? name = null, string? condition = null, string? discriminator = null)
    {
        string id = discriminator is null ? $"f_{sourceId}__{targetId}" : $"f_{sourceId}__{targetId}_{discriminator}";
        if (name is null && _nodes.TryGetValue(sourceId, out FlowNode? source) && source.OwedDefaultLabel is string owed)
        {
            name = owed;
            source.DefaultFlow = id;
            source.OwedDefaultLabel = null;
        }
        FlowEdge edge = new(id, sourceId, targetId, name, condition);
        _edges.Add(edge);
        return edge;
    }
}
