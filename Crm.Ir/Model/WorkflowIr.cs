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
    Variant,
    Unmapped
}

/// <summary>Which workflow and which step path a node came from. A single workflow's node has one; a combined node has many.</summary>
public sealed record StepSource(Guid WorkflowId, string Path);

/// <summary>A literal as written in the XAML (proves fidelity) and, where metadata allows, its label (what a human reads).</summary>
public sealed record LiteralValue(string Raw, string? Resolved);

/// <summary>A resolved condition. <see cref="Text"/> is for people; the parts are for comparison.</summary>
public sealed record Predicate(string Text, string? Entity, string? Attribute, string? Operator, IReadOnlyList<LiteralValue> Values);

/// <summary>One outgoing path of a condition, wait or variant split.</summary>
public sealed record Branch(string Label, Predicate? Predicate, IReadOnlyList<StepNode> Steps, IReadOnlyList<Guid> Members);

/// <summary>A field a step writes, with the value(s) written.</summary>
public sealed record FieldWrite(string Field, IReadOnlyList<LiteralValue> Values);

/// <summary>A named argument of a custom activity, captured verbatim (§4.2).</summary>
public sealed record NamedArgument(string Name, string Value);

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
    bool? IsCrmUiWorkflow);

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
    IrProvenance Provenance);
