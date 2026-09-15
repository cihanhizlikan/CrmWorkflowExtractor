using Crm.Ir.Model;

namespace Crm.Ir.Parsing;

public enum CoverageStatus
{
    /// <summary>Became (or is the evidence of) an IR step.</summary>
    Mapped,

    /// <summary>Machinery consumed by a step: variables, arguments, helper activities.</summary>
    Support,

    /// <summary>Not understood. Recorded, never dropped silently (§4.4).</summary>
    Unmapped
}

/// <summary>One XAML element seen by the parser, and what became of it.</summary>
public sealed record CoverageObservation(string Construct, CoverageStatus Status, string Path);

/// <summary>A literal string found anywhere in the XAML, for the sensitive-literal scan.</summary>
public sealed record XamlLiteral(string Path, string Text);

public sealed record ParseResult(
    IReadOnlyList<StepNode> Steps,
    WorkflowDependencies Dependencies,
    DataTouched DataTouched,
    IReadOnlyList<ParseWarning> Warnings,
    IReadOnlyList<CoverageObservation> Coverage,
    IReadOnlyList<XamlLiteral> Literals);
