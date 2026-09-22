namespace Crm.Bpmn.Graph;

/// <summary>
/// §6.2 layout. Classic workflows are shallow trees, so a recursive block layout is enough: a sequence lays its
/// blocks left to right, a split stacks its branches top to bottom between a split and a join gateway. Depth
/// drives X, branch index drives Y, and blocks never overlap because each reserves its whole bounding box.
/// </summary>
internal abstract class Block
{
    public const double HorizontalGap = 60;
    public const double VerticalGap = 120;  // room for a five-line caption above each branch’s first shape

    public abstract double Width { get; }

    public abstract double Height { get; }

    /// <summary>The node flow enters through; null for an empty block.</summary>
    public abstract FlowNode? Entry { get; }

    /// <summary>The node flow leaves through; null when the block ends the process (every path hits an end event) or is empty.</summary>
    public abstract FlowNode? Exit { get; }

    public abstract bool IsEmpty { get; }

    /// <summary>Places the block with its top-left corner at (x, y) and its entry/exit on the vertical centre line.</summary>
    public abstract void Place(double x, double y);
}

internal sealed class NodeBlock(FlowNode node) : Block
{
    public override double Width
    {
        get { return node.Width; }
    }

    public override double Height
    {
        get { return node.Height; }
    }

    public override FlowNode? Entry
    {
        get { return node; }
    }

    public override FlowNode? Exit
    {
        get { return node.IsEnd ? null : node; }
    }

    public override bool IsEmpty
    {
        get { return false; }
    }

    public override void Place(double x, double y)
    {
        node.X = x;
        node.Y = y;
    }
}

internal sealed class SequenceBlock(FlowGraph graph, IReadOnlyList<Block> blocks) : Block
{
    private readonly List<Block> _blocks = [.. blocks.Where(block => !block.IsEmpty)];

    public override double Width
    {
        get { return _blocks.Count == 0 ? 0 : _blocks.Sum(block => block.Width) + (HorizontalGap * (_blocks.Count - 1)); }
    }

    public override double Height
    {
        get { return _blocks.Count == 0 ? 0 : _blocks.Max(block => block.Height); }
    }

    public override FlowNode? Entry
    {
        get { return _blocks.Count == 0 ? null : _blocks[0].Entry; }
    }

    public override FlowNode? Exit
    {
        get { return _blocks.Count == 0 ? null : _blocks[^1].Exit; }
    }

    public override bool IsEmpty
    {
        get { return _blocks.Count == 0; }
    }

    /// <summary>
    /// Connects consecutive blocks. A block after one that ends the process is unreachable; it is still drawn,
    /// but no flow is invented into it.
    /// </summary>
    public void Connect()
    {
        for (int index = 1; index < _blocks.Count; index++)
        {
            FlowNode? from = _blocks[index - 1].Exit;
            FlowNode? to = _blocks[index].Entry;
            if (from is not null && to is not null)
            {
                graph.Connect(from.Id, to.Id);
            }
        }
    }

    public override void Place(double x, double y)
    {
        double height = Height;
        double cursor = x;
        foreach (Block block in _blocks)
        {
            block.Place(cursor, y + ((height - block.Height) / 2));
            cursor += block.Width + HorizontalGap;
        }
    }
}

/// <summary>One outgoing path of a split: its label, the condition on the entering flow, and its content.</summary>
internal sealed record SplitPath(string? Label, string? Condition, bool IsDefault, Block Content);

internal sealed class SplitBlock : Block
{
    private readonly FlowNode _split;
    private readonly FlowNode? _join;
    private readonly IReadOnlyList<SplitPath> _paths;

    public SplitBlock(FlowGraph graph, FlowNode split, FlowNode joinCandidate, IReadOnlyList<SplitPath> paths)
    {
        _split = split;
        _paths = paths;
        bool anyContinues = paths.Any(path => path.Content.IsEmpty || path.Content.Exit is not null);
        _join = anyContinues ? graph.Add(joinCandidate) : null;

        for (int index = 0; index < paths.Count; index++)
        {
            SplitPath path = paths[index];
            string discriminator = "b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            FlowNode? target = path.Content.Entry ?? _join;
            if (target is null)
            {
                continue;
            }
            FlowEdge edge = graph.Connect(split.Id, target.Id, path.Label, path.IsDefault ? null : path.Condition, discriminator);
            if (path.IsDefault)
            {
                split.DefaultFlow = edge.Id;
            }
            if (!path.Content.IsEmpty && path.Content.Exit is FlowNode exit && _join is not null)
            {
                graph.Connect(exit.Id, _join.Id, discriminator: discriminator);
            }
        }
    }

    private double BranchWidth
    {
        get { return _paths.Count == 0 ? 0 : _paths.Max(path => path.Content.IsEmpty ? 0 : path.Content.Width); }
    }

    private double BranchesHeight
    {
        get { return _paths.Sum(path => RowHeight(path)) + (VerticalGap * Math.Max(0, _paths.Count - 1)); }
    }

    public override double Width
    {
        get { return _split.Width + HorizontalGap + BranchWidth + (_join is null ? 0 : HorizontalGap + _join.Width); }
    }

    public override double Height
    {
        get { return Math.Max(BranchesHeight, _split.Height); }
    }

    public override FlowNode? Entry
    {
        get { return _split; }
    }

    public override FlowNode? Exit
    {
        get { return _join; }
    }

    public override bool IsEmpty
    {
        get { return false; }
    }

    public override void Place(double x, double y)
    {
        double height = Height;
        _split.X = x;
        _split.Y = y + ((height - _split.Height) / 2);
        double branchX = x + _split.Width + HorizontalGap;
        double rowY = y + ((height - BranchesHeight) / 2);
        foreach (SplitPath path in _paths)
        {
            double row = RowHeight(path);
            if (!path.Content.IsEmpty)
            {
                path.Content.Place(branchX, rowY + ((row - path.Content.Height) / 2));
            }
            rowY += row + VerticalGap;
        }
        if (_join is not null)
        {
            _join.X = branchX + BranchWidth + HorizontalGap;
            _join.Y = y + ((height - _join.Height) / 2);
        }
    }

    /// <summary>An empty branch still reserves a row, so its flow is drawn as a visible bypass rather than on top of another branch.</summary>
    private static double RowHeight(SplitPath path)
    {
        return path.Content.IsEmpty ? 36 : path.Content.Height;
    }
}
