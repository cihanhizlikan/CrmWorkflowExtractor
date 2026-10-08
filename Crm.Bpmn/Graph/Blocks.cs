namespace Crm.Bpmn.Graph;

/// <summary>
/// §6.2 layout. Classic workflows are shallow trees, so a recursive block layout is enough: a sequence lays its
/// blocks left to right, a split stacks its branches top to bottom between a split and a join gateway. Depth
/// drives X, branch index drives Y, and blocks never overlap because each reserves its whole bounding box.
///
/// <para>
/// Blocks are aligned on their SPINE — the height their flow enters and leaves at — and not on the middle of
/// their bounding box. The two are the same thing for most blocks, and for a split they are not: a split whose
/// branches all end the process is left through the diamond itself, along whichever row the continuation takes,
/// and that row is rarely the middle one. Aligning on bounding boxes put the diamond on the middle row instead,
/// where a branch was already standing, and the flow leaving it was drawn straight through that branch's shape.
/// Aligning on spines is also what keeps a chain of diamonds reading as one straight line.
/// </para>
/// </summary>
internal abstract class Block
{
    public const double HorizontalGap = 60;
    public const double VerticalGap = 120;  // room for a five-line caption above each branch’s first shape

    public abstract double Width { get; }

    public abstract double Height { get; }

    /// <summary>How far below the block's top edge its entry and exit sit — the line its flow comes in and goes out on.</summary>
    public abstract double Spine { get; }

    /// <summary>The node flow enters through; null for an empty block.</summary>
    public abstract FlowNode? Entry { get; }

    /// <summary>The node flow leaves through; null when the block ends the process (every path hits an end event) or is empty.</summary>
    public abstract FlowNode? Exit { get; }

    public abstract bool IsEmpty { get; }

    /// <summary>Places the block with its top-left corner at (x, y). Its spine then runs at <c>y + Spine</c>.</summary>
    public abstract void Place(double x, double y);
}

internal sealed class NodeBlock(FlowNode node) : Block
{
    public override double Width
    {
        get { return node.Width; }
    }

    /// <summary>
    /// The shape, plus the room its own text needs below it where the text is drawn outside the shape. Without
    /// that, the row beneath is placed as though an end event were thirty-six pixels tall when it is carrying a
    /// hundred pixels of name under it.
    /// </summary>
    public override double Height
    {
        get { return node.Height + LabelBox.BelowShape(node); }
    }

    public override double Spine
    {
        get { return node.Height / 2; }
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

    /// <summary>As deep as the deepest block above the spine, plus the deepest below it.</summary>
    public override double Height
    {
        get { return _blocks.Count == 0 ? 0 : Spine + _blocks.Max(block => block.Height - block.Spine); }
    }

    public override double Spine
    {
        get { return _blocks.Count == 0 ? 0 : _blocks.Max(block => block.Spine); }
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
        double spine = Spine;
        double cursor = x;
        foreach (Block block in _blocks)
        {
            block.Place(cursor, y + spine - block.Spine);
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
    private readonly FlowNode? _exit;
    private readonly SplitPath? _alone;
    private readonly IReadOnlyList<SplitPath> _paths;

    /// <summary>
    /// A join gateway is worth drawing only where paths actually MEET. Where a condition's branch ends the
    /// process — a Stop step — nothing comes back to be joined, and the join used to be drawn anyway: a diamond
    /// with one flow in and one out, which decides nothing and which a reader has to stop and read to discover
    /// that. Below two arriving paths there is no join, and the one path that carries on is the block's exit.
    /// </summary>
    public SplitBlock(FlowGraph graph, FlowNode split, FlowNode joinCandidate, IReadOnlyList<SplitPath> paths)
    {
        _split = split;
        _paths = paths;
        List<SplitPath> arriving = [.. paths.Where(path => path.Content.IsEmpty || path.Content.Exit is not null)];
        _join = arriving.Count > 1 ? graph.Add(joinCandidate) : null;
        SplitPath? alone = arriving.Count == 1 ? arriving[0] : null;
        _alone = alone;

        // The one arriving path leaves through the split itself when it is the bypass (there is nothing on it to
        // pass through), and otherwise through its own last step.
        _exit = _join ?? (alone is null ? null : alone.Content.IsEmpty ? split : alone.Content.Exit);
        if (alone is not null && alone.Content.IsEmpty)
        {
            split.OwedDefaultLabel = alone.Label;
        }

        for (int index = 0; index < paths.Count; index++)
        {
            SplitPath path = paths[index];
            string discriminator = "b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (path == alone && path.Content.IsEmpty)
            {
                continue;   // Its flow is the one leaving the split, and the sequence around it makes that.
            }
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

    /// <summary>
    /// The corridor between the diamond and its branches. Every one of this split's captions is drawn in it —
    /// beside the upright part of a branch's flow, or above the level part — so it is at least a caption wide.
    /// At sixty pixels a ninety-pixel caption reached past the corridor and over the branch's own shape.
    /// </summary>
    private double Corridor
    {
        get
        {
            bool captioned = _paths.Any(path => !string.IsNullOrEmpty(path.Label));
            return captioned ? Math.Max(HorizontalGap, LabelBox.WrapWidth + (LabelBox.Gap * 2) + 8) : HorizontalGap;
        }
    }

    /// <summary>
    /// The space between one branch row and the next. A caption sits between the diamond's line and its branch's,
    /// centred there and growing both ways, so the rows have to stand at least a caption apart — and a condition
    /// is never shortened, so that can be a hundred and sixty pixels rather than the five lines this gap was
    /// written for.
    /// </summary>
    private double RowGap
    {
        get { return Math.Max(VerticalGap, _paths.Max(path => LabelBox.HeightOf(path.Label)) + 16); }
    }

    private double BranchesHeight
    {
        get { return _paths.Sum(path => RowHeight(path)) + (RowGap * Math.Max(0, _paths.Count - 1)); }
    }

    public override double Width
    {
        get { return _split.Width + Corridor + BranchWidth + (_join is null ? 0 : HorizontalGap + _join.Width); }
    }

    public override double Height
    {
        get { return Bottom - Top; }
    }

    public override double Spine
    {
        get { return SpineInRows - Top; }
    }

    /// <summary>
    /// The height the flow leaves this split at, measured from the top of the branch rows.
    ///
    /// <para>
    /// Where the paths meet at a join, both diamonds sit in the middle, as they always did. Where they do not —
    /// every branch ends the process, and what carries on is one path of its own — the split sits on THAT path's
    /// row, because that is the row its outgoing flow runs along. Centring it instead put it on whichever row
    /// happened to be in the middle, and the flow was then drawn through that row's shape.
    /// </para>
    /// </summary>
    private double SpineInRows
    {
        get { return ExitRow ?? (BranchesHeight / 2); }
    }

    /// <summary>The centre of the row belonging to the path the block is left through, or null where there is none.</summary>
    private double? ExitRow
    {
        get
        {
            if (_join is not null || _alone is null)
            {
                return null;
            }
            double top = 0;
            foreach (SplitPath path in _paths)
            {
                if (path == _alone)
                {
                    return top + (RowHeight(path) / 2);
                }
                top += RowHeight(path) + RowGap;
            }
            return null;
        }
    }

    /// <summary>How far the diamond and the join reach above the rows, when they are taller than the row they sit on.</summary>
    private double Top
    {
        get { return Math.Min(0, SpineInRows - (Math.Max(_split.Height, _join?.Height ?? 0) / 2)); }
    }

    private double Bottom
    {
        get { return Math.Max(BranchesHeight, SpineInRows + (Math.Max(_split.Height, _join?.Height ?? 0) / 2)); }
    }

    public override FlowNode? Entry
    {
        get { return _split; }
    }

    public override FlowNode? Exit
    {
        get { return _exit; }
    }

    public override bool IsEmpty
    {
        get { return false; }
    }

    public override void Place(double x, double y)
    {
        double spine = y + Spine;
        _split.X = x;
        _split.Y = spine - (_split.Height / 2);
        double branchX = x + _split.Width + Corridor;
        double gap = RowGap;
        double rowY = y - Top;
        foreach (SplitPath path in _paths)
        {
            double row = RowHeight(path);
            if (!path.Content.IsEmpty)
            {
                // Each branch is aligned on ITS spine inside its row, so a branch that is itself a split keeps
                // its own diamond on the row rather than wherever its bounding box happens to be centred.
                path.Content.Place(branchX, rowY + ((row - path.Content.Height) / 2));
            }
            rowY += row + gap;
        }
        if (_join is not null)
        {
            _join.X = branchX + BranchWidth + HorizontalGap;
            _join.Y = spine - (_join.Height / 2);
        }
    }

    /// <summary>An empty branch still reserves a row, so its flow is drawn as a visible bypass rather than on top of another branch.</summary>
    private static double RowHeight(SplitPath path)
    {
        return path.Content.IsEmpty ? 36 : path.Content.Height;
    }
}
