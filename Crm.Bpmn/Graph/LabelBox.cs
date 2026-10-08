namespace Crm.Bpmn.Graph;

/// <summary>
/// The box a viewer draws a label in.
///
/// <para>
/// It lives here, and not in the serializer that writes the bounds, because the LAYOUT has to reserve the room
/// the serializer will then use. Measuring in one place and spacing by a constant in the other is what let a
/// hundred-and-sixty-pixel caption be drawn into a hundred-and-twenty-pixel gap, on top of its neighbour.
/// </para>
/// </summary>
internal static class LabelBox
{
    /// <summary>Viewers wrap an external label at their own fixed width — 90 pixels in bpmn.io.</summary>
    public const double WrapWidth = 90;

    /// <summary>Clear of the line by this much, so a caption reads as beside it rather than written through it.</summary>
    public const double Gap = 6;

    /// <summary>
    /// Room for the text as a viewer actually draws it: wrapped at <see cref="WrapWidth"/> and centred on these
    /// bounds, growing up and down, so the height is what matters. About 6.2 pixels a character at the 11-pixel
    /// font. Twelve lines at most — a condition is never shortened, so its box has to be able to hold one.
    /// </summary>
    public static (double Width, double Height) Measure(string text)
    {
        double lines = Math.Min(12, Math.Ceiling(((text.Length * 6.2) + 4) / WrapWidth));
        return (WrapWidth, Math.Round((lines * 13) + 6));
    }

    /// <summary>The height of a caption, or zero where there is no caption to draw.</summary>
    public static double HeightOf(string? text)
    {
        return string.IsNullOrEmpty(text) ? 0 : Measure(text).Height;
    }

    /// <summary>
    /// Whether a shape's text is drawn OUTSIDE it, below it. A task is a box and holds its own text; a gateway is
    /// a fifty-pixel diamond and an event a thirty-six-pixel circle, and a viewer paints their names underneath.
    /// </summary>
    public static bool CarriesTextBelow(FlowNode node)
    {
        return node.Name.Length > 0 && node.Type is FlowNodeType.ExclusiveGateway or FlowNodeType.EventBasedGateway
            or FlowNodeType.StartEvent or FlowNodeType.EndEvent or FlowNodeType.TerminateEndEvent
            or FlowNodeType.ConditionalCatchEvent or FlowNodeType.TimerCatchEvent;
    }

    /// <summary>
    /// The room a shape's own text needs below it. The LAYOUT has to reserve it: an end event is thirty-six
    /// pixels of circle and can carry a hundred of text under it, and the row below was being placed as though
    /// the text were not there.
    /// </summary>
    public static double BelowShape(FlowNode node)
    {
        return CarriesTextBelow(node) ? Gap + Measure(node.Name).Height : 0;
    }
}
