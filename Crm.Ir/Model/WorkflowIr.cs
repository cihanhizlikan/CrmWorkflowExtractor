namespace Crm.Ir.Model;

/// <summary>§5.1 step kinds. <see cref="Variant"/> exists only in a combined workflow: a split between member workflows.</summary>
public enum StepKind
{
    Sequence,
    Condition,
    WaitCondition,
    Timeout,
    CreateRecord,
    UpdateRecord,
    AssignRecord,
    ChangeStatus,
    SendEmail,
    StartChildWorkflow,
    CustomActivity,
    StopWorkflow,
    Stage,
    /// <summary>A dialog page: a prompt shown to the user and the response collected.</summary>
    UserInteraction,
    /// <summary>A dialog query: records retrieved to offer as response choices.</summary>
    DataQuery,
    /// <summary>A business-rule action on the form (<see cref="StepNode.Detail"/> says which: show/hide, require, lock, set value, message).</summary>
    FormAction,
    Variant,
    Unmapped
}

/// <summary>Which workflow and which step path a node came from. A single workflow's node has one; a combined node has many.</summary>
public sealed record StepSource(Guid WorkflowId, string Path);

/// <summary>A literal as written in the XAML (proves fidelity) and, where metadata allows, its label (what a human reads).</summary>
public sealed record LiteralValue(string Raw, string? Resolved)
{
    /// <summary>
    /// What <see cref="Raw"/> reads as when the definition does NOT fix the value and CRM works it out as it
    /// runs. It lives on the model rather than on the parser that writes it, because every stage after the parser
    /// has to be able to tell the two apart — a diagram shows a value the definition fixes and names the field
    /// alone otherwise.
    /// </summary>
    public const string Dynamic = "<dynamic>";

    /// <summary>In a combined workflow: which member workflows set this value. Absent for a single workflow.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? Workflows { get; init; }
}

/// <summary>A resolved condition. <see cref="Text"/> is for people; the parts are for comparison.</summary>
public sealed record Predicate(string Text, string? Entity, string? Attribute, string? Operator, IReadOnlyList<LiteralValue> Values)
{
    /// <summary>
    /// What is being tested, on its own: the field, or whatever stood in for it. Carried rather than cut back out
    /// of <see cref="Text"/> — the only way to find it there was to search for the operator's name, which stopped
    /// working the moment the operator was written in words.
    /// </summary>
    public string Subject { get; init; } = "";
}

/// <summary>One outgoing path of a condition, wait or variant split.</summary>
public sealed record Branch(string Label, Predicate? Predicate, IReadOnlyList<StepNode> Steps, IReadOnlyList<Guid> Members);

/// <summary>A field a step writes, with the value(s) written.</summary>
public sealed record FieldWrite(string Field, IReadOnlyList<LiteralValue> Values);

/// <summary>A named argument of a custom activity, captured verbatim (§4.2).</summary>
public sealed record NamedArgument(string Name, string Value)
{
    /// <summary>
    /// Whether the activity WRITES this one back. The difference between what a step is given and what it hands
    /// back is the whole of its contract as far as anything outside the compiled code can see, and the XAML says
    /// which is which: <c>OutArgument</c> against <c>InArgument</c>. Null where the shape does not say.
    /// </summary>
    public bool? Output { get; init; }
}

/// <summary>
/// One argument the workflow ITSELF declares — what an Action takes and returns. For a plain workflow these are
/// CRM's own plumbing; for an Action they are the signature a caller sees, and the only description of it that
/// exists outside the compiled code.
/// </summary>
public sealed record WorkflowParameter(string Name, string Type, bool Output);

public sealed record StepNode(
    string Path,
    StepKind Kind,
    string DisplayName,
    string? Entity,
    IReadOnlyList<FieldWrite> Fields,
    IReadOnlyList<Branch> Branches,
    string? Detail,
    IReadOnlyList<NamedArgument> Arguments,
    string? Construct,
    IReadOnlyList<StepSource> Sources);

public sealed record WorkflowIdentity(
    Guid WorkflowId,
    string Name,
    string? UniqueName,
    string Category,
    string Type,
    string? PrimaryEntity,
    string Mode,
    string Scope,
    string State,
    bool? IsChildProcess,
    bool? IsOnDemand,
    Guid? Owner,
    string? CreatedOn,
    string? ModifiedOn,
    long? VersionNumber,
    bool? IsCrmUiWorkflow)
{
    /// <summary>
    /// True when CRM reports the workflow as part of a managed solution: it was shipped with the product or with a
    /// partner solution, not written here. Null when the server did not answer the column.
    /// </summary>
    public bool? IsManaged { get; init; }

    /// <summary>The owner's display name, as CRM formats the lookup. The owner may be a user or a team.</summary>
    public string? OwnerName { get; init; }

    /// <summary>The business unit owning the workflow RECORD: what a role's read depth is measured against.</summary>
    public string? OwningBusinessUnit { get; init; }
}

/// <summary>CRM's own plumbing arguments, present on every definition and telling a reader nothing about it.</summary>
public static class WorkflowPlumbing
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "InputEntities", "CreatedEntities", "UpdatedEntities", "DeletedEntities", "PrimaryEntity"
    };
}

public sealed record WorkflowTrigger(
    bool OnCreate,
    bool OnDelete,
    IReadOnlyList<string> OnUpdateFields,
    string? CreateStage,
    string? UpdateStage,
    string? DeleteStage,
    string RunAs,
    bool OnDemand);

public sealed record WorkflowDependencies(IReadOnlyList<Guid> ChildWorkflowCalls, IReadOnlyList<string> CustomActivities);

public sealed record DataTouched(IReadOnlyList<string> EntitiesRead, IReadOnlyList<string> EntitiesWritten, IReadOnlyList<string> FieldsRead, IReadOnlyList<string> FieldsWritten);

public sealed record ParseWarning(string Path, string Message);

public sealed record IrProvenance(string SourceFile, string SourceFileSha256, string ExtractedAtUtc, string ToolVersion);

/// <summary>
/// §5.1: one workflow, normalized. BPMN and similarity both derive from this and never from each other. Serialized
/// with declaration-ordered properties and deterministic collection order so re-runs diff cleanly.
/// </summary>
public sealed record WorkflowIr(
    WorkflowIdentity Identity,
    WorkflowTrigger Trigger,
    IReadOnlyList<StepNode> Steps,
    WorkflowDependencies Dependencies,
    DataTouched DataTouched,
    IReadOnlyList<ParseWarning> Warnings,
    IrProvenance Provenance)
{
    /// <summary>What this workflow itself takes and returns, with CRM's plumbing left out. Empty for most.</summary>
    public IReadOnlyList<WorkflowParameter> Parameters { get; init; } = [];
}
