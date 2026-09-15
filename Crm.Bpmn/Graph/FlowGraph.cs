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

    public IReadOnlyList<StepSource> Sources { get; init; } = [];

    /// <summary>For a custom activity: its assembly-qualified type, carried in the extension block.</summary>
    public string? ActivityType { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width
    {
        get { return Size(Type).Width; }
    }

    public double Height
    {
        get { return Size(Type).Height; }
    }

    public bool IsEnd
    {
        get { return Type is FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent; }
    }

    public static (double Width, double Height) Size(FlowNodeType type)
    {
        return type switch
        {
            FlowNodeType.StartEvent or FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent
                or FlowNodeType.ConditionalCatchEvent or FlowNodeType.TimerCatchEvent => (36, 36),
            FlowNodeType.ExclusiveGateway or FlowNodeType.EventBasedGateway => (50, 50),
            _ => (130, 70)
        };
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

    public FlowEdge Connect(string sourceId, string targetId, string? name = null, string? condition = null, string? discriminator = null)
    {
        string id = discriminator is null ? $"f_{sourceId}__{targetId}" : $"f_{sourceId}__{targetId}_{discriminator}";
        FlowEdge edge = new(id, sourceId, targetId, name, condition);
        _edges.Add(edge);
        return edge;
    }
}
