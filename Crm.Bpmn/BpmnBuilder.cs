using System.Globalization;
using System.Text;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;

namespace Crm.Bpmn;

/// <summary>A laid-out process ready to serialize.</summary>
public sealed record BpmnProcess(string ProcessId, string Name, string Documentation, FlowGraph Graph, IReadOnlyList<StepSource> Sources);

/// <summary>
/// IR → BPMN flow graph, per the §6.1 mapping. Element ids derive from the id base (the workflow id, or a cluster
/// id for a combined workflow) plus the IR step path — never a counter — so unchanged input gives identical files.
/// </summary>
public sealed class BpmnBuilder
{
    private readonly FlowGraph _graph = new();
    private readonly string _idBase;
    private readonly string _workflowName;

    private BpmnBuilder(string idBase, string workflowName)
    {
        _idBase = idBase;
        _workflowName = workflowName;
    }

    public static string ProcessIdFor(Guid workflowId)
    {
        return "wf_" + workflowId.ToString("N");
    }

    public static BpmnProcess Build(WorkflowIr ir)
    {
        return Build(ProcessIdFor(ir.Identity.WorkflowId), ir.Identity.Name, ir, [new StepSource(ir.Identity.WorkflowId, "")]);
    }

    /// <summary>Builds from any IR; <paramref name="processId"/> also seeds every element id.</summary>
    public static BpmnProcess Build(string processId, string name, WorkflowIr ir, IReadOnlyList<StepSource> sources)
    {
        BpmnBuilder builder = new(processId[(processId.IndexOf('_', StringComparison.Ordinal) + 1)..], name);
        FlowNode start = builder._graph.Add(new FlowNode(builder.Id("start"), FlowNodeType.StartEvent, StartName(ir.Trigger))
        {
            Documentation = TriggerDocumentation(ir),
            Expression = TriggerCondition(ir.Trigger)
        });
        Block body = builder.Sequence(ir.Steps);
        List<Block> parts = [new NodeBlock(start), body];
        if (body.IsEmpty || body.Exit is not null)
        {
            parts.Add(new NodeBlock(builder._graph.Add(new FlowNode(builder.Id("end"), FlowNodeType.EndEvent, "End"))));
        }
        SequenceBlock process = new(builder._graph, parts);
        process.Connect();
        process.Place(40, 40);

        string documentation = $"Source workflow: {ir.Identity.Name} ({ir.Identity.WorkflowId:D}). Category {ir.Identity.Category}, "
            + $"entity {ir.Identity.PrimaryEntity ?? "none"}, {ir.Identity.Mode}, state {ir.Identity.State}. Descriptive model generated from CRM XAML; not executable.";
        return new BpmnProcess(processId, name, documentation, builder._graph, sources);
    }

    private SequenceBlock Sequence(IReadOnlyList<StepNode> steps)
    {
        List<Block> blocks = [.. steps.Select(Step)];
        SequenceBlock sequence = new(_graph, blocks);
        sequence.Connect();
        return sequence;
    }

    private Block Step(StepNode step)
    {
        return step.Kind switch
        {
            StepKind.Sequence => Sequence([.. step.Branches.SelectMany(branch => branch.Steps)]),
            StepKind.Condition => Split(step, FlowNodeType.ExclusiveGateway, "Otherwise"),
            StepKind.Variant => Split(step, FlowNodeType.ExclusiveGateway, null),
            StepKind.WaitCondition => Wait(step),
            _ => new NodeBlock(_graph.Add(Task(step)))
        };
    }

    private FlowNode Task(StepNode step)
    {
        (FlowNodeType type, string? expression) = step.Kind switch
        {
            StepKind.CreateRecord or StepKind.UpdateRecord or StepKind.AssignRecord or StepKind.ChangeStatus or StepKind.CustomActivity => (FlowNodeType.ServiceTask, null as string),
            StepKind.SendEmail => (FlowNodeType.SendTask, null),
            StepKind.StartChildWorkflow => (FlowNodeType.CallActivity, Guid.TryParse(step.Detail, out Guid child) ? ProcessIdFor(child) : null),
            StepKind.StopWorkflow => (step.Detail == "Canceled" ? FlowNodeType.TerminateEndEvent : FlowNodeType.EndEvent, null),
            StepKind.Timeout => (FlowNodeType.TimerCatchEvent, step.Detail),
            _ => (FlowNodeType.Task, null)
        };
        return new FlowNode(Id(step.Path), type, TaskName(step))
        {
            Documentation = StepDocumentation(step),
            Expression = expression,
            Sources = step.Sources,
            ActivityType = step.Kind == StepKind.CustomActivity ? step.Detail : null
        };
    }

    private SplitBlock Split(StepNode step, FlowNodeType gatewayType, string? defaultLabel)
    {
        FlowNode split = _graph.Add(new FlowNode(Id(step.Path, "split"), gatewayType, Truncate(step.DisplayName.Length > 0 ? step.DisplayName : step.Kind.ToString(), 60))
        {
            Documentation = StepDocumentation(step),
            Sources = step.Sources
        });
        FlowNode join = new(Id(step.Path, "join"), FlowNodeType.ExclusiveGateway, "");
        List<SplitPath> paths = [];
        foreach (Branch branch in step.Branches)
        {
            bool isDefault = defaultLabel is not null && branch.Predicate is null && branch.Label == defaultLabel;
            string label = step.Kind == StepKind.Variant ? "Variant: " + branch.Label : branch.Label;
            paths.Add(new SplitPath(Truncate(label, 60), label, isDefault, Sequence(branch.Steps)));
        }
        if (step.Kind == StepKind.Condition && !step.Branches.Any(branch => branch.Predicate is null && branch.Label == defaultLabel))
        {
            // A condition with no otherwise-branch continues when no branch holds: an explicit, labelled bypass.
            paths.Add(new SplitPath("(no condition met)", null, true, new SequenceBlock(_graph, [])));
        }
        return new SplitBlock(_graph, split, join, paths);
    }

    /// <summary>A single wait is one conditional catch event; a wait with several outcomes (e.g. a timeout) is an event-based gateway.</summary>
    private Block Wait(StepNode step)
    {
        if (step.Branches.Count <= 1)
        {
            Branch? only = step.Branches.FirstOrDefault();
            FlowNode waitEvent = _graph.Add(new FlowNode(Id(step.Path), FlowNodeType.ConditionalCatchEvent, Truncate("Wait: " + (only?.Label ?? step.DisplayName), 60))
            {
                Documentation = StepDocumentation(step),
                Expression = only?.Predicate?.Text ?? only?.Label ?? "condition",
                Sources = step.Sources
            });
            List<Block> blocks = [new NodeBlock(waitEvent)];
            if (only is not null)
            {
                blocks.Add(Sequence(only.Steps));
            }
            SequenceBlock waitSequence = new(_graph, blocks);
            waitSequence.Connect();
            return waitSequence;
        }

        FlowNode gateway = _graph.Add(new FlowNode(Id(step.Path, "split"), FlowNodeType.EventBasedGateway, Truncate(step.DisplayName, 60))
        {
            Documentation = StepDocumentation(step),
            Sources = step.Sources
        });
        List<SplitPath> paths = [];
        for (int index = 0; index < step.Branches.Count; index++)
        {
            Branch branch = step.Branches[index];
            bool timer = branch.Steps.Count > 0 && branch.Steps[0].Kind == StepKind.Timeout;
            IReadOnlyList<StepNode> rest = timer ? [.. branch.Steps.Skip(1)] : branch.Steps;
            FlowNode catchEvent = _graph.Add(new FlowNode(Id(step.Path, "b" + index.ToString(CultureInfo.InvariantCulture) + "_event"),
                timer ? FlowNodeType.TimerCatchEvent : FlowNodeType.ConditionalCatchEvent, Truncate(branch.Label, 60))
            {
                Expression = timer ? branch.Steps[0].Detail : branch.Predicate?.Text ?? branch.Label,
                Sources = timer ? branch.Steps[0].Sources : step.Sources
            });
            SequenceBlock content = new(_graph, [new NodeBlock(catchEvent), Sequence(rest)]);
            content.Connect();
            paths.Add(new SplitPath(null, null, false, content));
        }
        return new SplitBlock(_graph, gateway, new FlowNode(Id(step.Path, "join"), FlowNodeType.ExclusiveGateway, ""), paths);
    }

    private string Id(string path, string? suffix = null)
    {
        string safePath = path.Replace('/', '_');
        return suffix is null ? $"n_{_idBase}_{safePath}" : $"n_{_idBase}_{safePath}_{suffix}";
    }

    private string StepDocumentation(StepNode step)
    {
        StringBuilder text = new();
        if (step.Kind == StepKind.Unmapped)
        {
            text.Append("UNMAPPED: the construct '").Append(step.Construct).Append("' was not understood by the parser; see parse-coverage.md. ");
        }
        text.Append(step.Kind).Append(": ").Append(step.DisplayName.Length == 0 ? "(unnamed)" : step.DisplayName).Append('.');
        if (step.Entity is not null)
        {
            text.Append(" Entity ").Append(step.Entity).Append('.');
        }
        foreach (FieldWrite field in step.Fields)
        {
            text.Append(" Sets ").Append(field.Field).Append(" = ")
                .Append(string.Join(" | ", field.Values.Select(value => value.Resolved is null ? value.Raw : $"{value.Resolved} ({value.Raw})"))).Append('.');
        }
        if (step.Detail is not null)
        {
            text.Append(" Detail: ").Append(step.Detail).Append('.');
        }
        foreach (NamedArgument argument in step.Arguments)
        {
            text.Append(" Argument ").Append(argument.Name).Append(" = ").Append(argument.Value).Append('.');
        }
        text.Append(" Source: ").Append(string.Join("; ", step.Sources.Select(source => $"{_workflowName} ({source.WorkflowId:D}) step {source.Path}"))).Append('.');
        return text.ToString();
    }

    private static string TaskName(StepNode step)
    {
        string verb = step.Kind switch
        {
            StepKind.CreateRecord => "Create",
            StepKind.UpdateRecord => "Update",
            StepKind.AssignRecord => "Assign",
            StepKind.ChangeStatus => "Change status",
            StepKind.SendEmail => "Send email",
            StepKind.StartChildWorkflow => "Start child workflow",
            StepKind.CustomActivity => "Custom activity",
            StepKind.StopWorkflow => "Stop",
            StepKind.Timeout => "Timeout",
            StepKind.Unmapped => "UNMAPPED " + step.Construct,
            _ => step.Kind.ToString()
        };
        string subject = step.DisplayName.Length > 0 ? step.DisplayName : step.Entity ?? "";
        return Truncate(subject.Length == 0 ? verb : $"{verb}: {subject}", 80);
    }

    private static string StartName(WorkflowTrigger trigger)
    {
        List<string> parts = [];
        if (trigger.OnCreate)
        {
            parts.Add("create");
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add("update");
        }
        if (trigger.OnDelete)
        {
            parts.Add("delete");
        }
        if (trigger.OnDemand)
        {
            parts.Add("on demand");
        }
        return parts.Count == 0 ? "Start" : "On " + string.Join(" / ", parts);
    }

    private static string? TriggerCondition(WorkflowTrigger trigger)
    {
        return trigger.OnCreate || trigger.OnDelete || trigger.OnUpdateFields.Count > 0 ? TriggerText(trigger) : null;
    }

    private static string TriggerText(WorkflowTrigger trigger)
    {
        List<string> parts = [];
        if (trigger.OnCreate)
        {
            parts.Add("record created" + (trigger.CreateStage is null ? "" : $" ({trigger.CreateStage})"));
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add($"fields updated: {string.Join(", ", trigger.OnUpdateFields)}" + (trigger.UpdateStage is null ? "" : $" ({trigger.UpdateStage})"));
        }
        if (trigger.OnDelete)
        {
            parts.Add("record deleted" + (trigger.DeleteStage is null ? "" : $" ({trigger.DeleteStage})"));
        }
        return string.Join("; ", parts);
    }

    private static string TriggerDocumentation(WorkflowIr ir)
    {
        string entity = ir.Identity.PrimaryEntity ?? "none";
        string automatic = ir.Trigger.OnCreate || ir.Trigger.OnDelete || ir.Trigger.OnUpdateFields.Count > 0
            ? $"Triggered on {entity} when {TriggerText(ir.Trigger)}."
            : "No automatic trigger.";
        string onDemand = ir.Trigger.OnDemand ? " Can be started by a user (on demand)." : "";
        return $"{automatic}{onDemand} Runs as {ir.Trigger.RunAs}.";
    }

    private static string Truncate(string text, int length)
    {
        string flat = text.ReplaceLineEndings(" ");
        return flat.Length <= length ? flat : flat[..(length - 1)] + "…";
    }
}
