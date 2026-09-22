using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;

namespace Crm.Bpmn;

/// <summary>A laid-out process ready to serialize.</summary>
public sealed record BpmnProcess(string ProcessId, string Name, string Documentation, FlowGraph Graph, IReadOnlyList<StepSource> Sources)
{
    /// <summary>The header note drawn above the diagram: what this process is, what starts it, and how complete the model is.</summary>
    public string? Note { get; init; }

    /// <summary>Where that note sits, and how big it is.</summary>
    public (double X, double Y, double Width, double Height) NoteBounds { get; init; }
}

/// <summary>
/// IR → BPMN flow graph, per the §6.1 mapping. Element ids derive from the id base (the workflow id, or a cluster
/// id for a combined workflow) plus the IR step path — never a counter — so unchanged input gives identical files.
/// </summary>
public sealed partial class BpmnBuilder
{
    private readonly FlowGraph _graph = new();
    private readonly string _idBase;
    private readonly string _workflowName;
    private readonly IReadOnlyDictionary<Guid, string> _memberNames;
    private readonly IReadOnlyDictionary<Guid, string> _workflowNames;

    private BpmnBuilder(string idBase, string workflowName, IReadOnlyDictionary<Guid, string>? memberNames, IReadOnlyDictionary<Guid, string>? workflowNames)
    {
        _idBase = idBase;
        _workflowName = workflowName;
        _memberNames = memberNames ?? new Dictionary<Guid, string>();
        _workflowNames = workflowNames ?? new Dictionary<Guid, string>();
    }

    public static string ProcessIdFor(Guid workflowId)
    {
        return "wf_" + workflowId.ToString("N");
    }

    public static BpmnProcess Build(WorkflowIr ir, IReadOnlyDictionary<Guid, string>? workflowNames = null)
    {
        return Build(ProcessIdFor(ir.Identity.WorkflowId), ir.Identity.Name, ir, [new StepSource(ir.Identity.WorkflowId, "")], null, workflowNames);
    }

    /// <summary>
    /// Builds from any IR; <paramref name="processId"/> also seeds every element id. For a combined workflow,
    /// <paramref name="memberNames"/> names each source workflow in the documentation.
    /// </summary>
    public static BpmnProcess Build(string processId, string name, WorkflowIr ir, IReadOnlyList<StepSource> sources,
        IReadOnlyDictionary<Guid, string>? memberNames = null, IReadOnlyDictionary<Guid, string>? workflowNames = null)
    {
        BpmnBuilder builder = new(processId[(processId.IndexOf('_', StringComparison.Ordinal) + 1)..], name, memberNames, workflowNames);
        FlowNode start = builder._graph.Add(new FlowNode(builder.Id("start"), FlowNodeType.StartEvent, StartName(ir.Trigger))
        {
            Documentation = TriggerDocumentation(ir),
            Expression = TriggerCondition(ir.Trigger)
        });
        Block body = builder.Sequence(ir.Steps);
        List<Block> parts = [new NodeBlock(start), body];
        if (body.IsEmpty || body.Exit is not null)
        {
            parts.Add(new NodeBlock(builder._graph.Add(new FlowNode(builder.Id("end"), FlowNodeType.EndEvent, "Bitiş"))));
        }
        SequenceBlock process = new(builder._graph, parts);
        process.Connect();

        // The note is a shape like any other, so the diagram starts below it and nothing is drawn over it.
        string note = HeaderNote(ir, name);
        double noteHeight = (note.Split('\n').Length * 15) + 16;
        // Clear of the note, with room for a branch caption above the first row of shapes.
        process.Place(40, 40 + noteHeight + 120);

        string documentation = $"Kaynak iş akışı: {ir.Identity.Name} ({ir.Identity.WorkflowId:D}). Kategori {ir.Identity.Category}, "
            + $"varlık {ir.Identity.PrimaryEntity ?? "yok"}, {ir.Identity.Mode}, durum {ir.Identity.State}. CRM XAML dosyasından üretilmiş açıklayıcı modeldir; çalıştırılabilir değildir.";
        return new BpmnProcess(processId, name, documentation, builder._graph, sources)
        {
            Note = note,
            NoteBounds = (40, 40, 560, noteHeight)
        };
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
            StepKind.Condition => Split(step, FlowNodeType.ExclusiveGateway, "Aksi hâlde"),
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
            StepKind.DataQuery => (FlowNodeType.ServiceTask, null),
            StepKind.UserInteraction => (FlowNodeType.UserTask, null),
            StepKind.FormAction => (FlowNodeType.BusinessRuleTask, null),
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
        FlowNode split = _graph.Add(new FlowNode(Id(step.Path, "split"), gatewayType, Truncate(GatewayName(step), 40))
        {
            Documentation = StepDocumentation(step),
            Sources = step.Sources
        });
        FlowNode join = new(Id(step.Path, "join"), FlowNodeType.ExclusiveGateway, "");
        List<SplitPath> paths = [];
        foreach (Branch branch in step.Branches)
        {
            bool isDefault = defaultLabel is not null && branch.Predicate is null && branch.Label == defaultLabel;
            string label = step.Kind == StepKind.Variant ? "Çeşitleme: " + branch.Label : branch.Label;
            paths.Add(new SplitPath(Truncate(label, 60), label, isDefault, Sequence(branch.Steps)));
        }
        if (step.Kind == StepKind.Condition && !step.Branches.Any(branch => branch.Predicate is null && branch.Label == defaultLabel))
        {
            // A condition with no otherwise-branch continues when no branch holds: an explicit, labelled bypass.
            paths.Add(new SplitPath("(hiçbir koşul sağlanmazsa)", null, true, new SequenceBlock(_graph, [])));
        }
        return new SplitBlock(_graph, split, join, paths);
    }

    /// <summary>
    /// What a diamond is asking. The author's own name for the step when there is one; otherwise the field the
    /// branches test, because CRM's internal id (<c>ConditionStep4</c>) tells a reader nothing. The full condition
    /// of each branch is on that branch's own flow, not here.
    /// </summary>
    private static string GatewayName(StepNode step)
    {
        if (step.DisplayName.Length > 0 && !InternalStepId().IsMatch(step.DisplayName))
        {
            return step.DisplayName;
        }
        Predicate? first = step.Branches.Select(branch => branch.Predicate).FirstOrDefault(predicate => predicate is not null);
        if (first is { Entity: string entity, Attribute: string attribute })
        {
            return $"{entity}.{attribute}?";
        }
        return step.Kind == StepKind.Variant ? "Üyeler ayrışıyor" : "Koşul";
    }

    /// <summary>A single wait is one conditional catch event; a wait with several outcomes (e.g. a timeout) is an event-based gateway.</summary>
    private Block Wait(StepNode step)
    {
        if (step.Branches.Count <= 1)
        {
            Branch? only = step.Branches.FirstOrDefault();
            FlowNode waitEvent = _graph.Add(new FlowNode(Id(step.Path), FlowNodeType.ConditionalCatchEvent, Truncate("Bekle: " + (only?.Label ?? step.DisplayName), 60))
            {
                Documentation = StepDocumentation(step),
                Expression = only?.Predicate?.Text ?? only?.Label ?? "koşul",
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
            text.Append("OKUNAMADI: '").Append(step.Construct).Append("' yapısını ayrıştırıcı anlamadı; bkz. ayristirma-kapsami.md. ");
        }
        text.Append(Kind(step.Kind)).Append(": ").Append(step.DisplayName.Length == 0 ? "(adsız)" : step.DisplayName).Append('.');
        if (step.Entity is not null)
        {
            text.Append(" Varlık: ").Append(step.Entity).Append('.');
        }
        foreach (FieldWrite field in step.Fields)
        {
            text.Append(" Yazar: ").Append(field.Field).Append(" = ")
                .Append(string.Join(" | ", field.Values.Select(value => value.Resolved is null ? value.Raw : $"{value.Resolved} ({value.Raw})"))).Append('.');
        }
        if (step.Detail is not null)
        {
            text.Append(" Ayrıntı: ").Append(step.Detail).Append('.');
        }
        foreach (NamedArgument argument in step.Arguments)
        {
            text.Append(" Bağımsız değişken: ").Append(argument.Name).Append(" = ").Append(argument.Value).Append('.');
        }
        text.Append(" Kaynak: ").Append(string.Join("; ", step.Sources.Select(source => $"{_memberNames.GetValueOrDefault(source.WorkflowId, _workflowName)} ({source.WorkflowId:D}) adım {source.Path}"))).Append('.');
        return text.ToString();
    }

    /// <summary>The step kinds as the documentation names them.</summary>
    private static string Kind(StepKind kind)
    {
        return kind switch
        {
            StepKind.Sequence => "Sıra",
            StepKind.Condition => "Koşul",
            StepKind.WaitCondition => "Bekleme koşulu",
            StepKind.Timeout => "Bekleme",
            StepKind.CreateRecord => "Kayıt oluşturma",
            StepKind.UpdateRecord => "Kayıt güncelleme",
            StepKind.AssignRecord => "Sahip değiştirme",
            StepKind.ChangeStatus => "Durum değiştirme",
            StepKind.SendEmail => "E-posta gönderme",
            StepKind.StartChildWorkflow => "Alt iş akışı başlatma",
            StepKind.CustomActivity => "Özel etkinlik",
            StepKind.StopWorkflow => "Durdurma",
            StepKind.Stage => "Aşama",
            StepKind.UserInteraction => "Diyalog sayfası",
            StepKind.DataQuery => "Veri sorgulama",
            StepKind.FormAction => "Form eylemi",
            StepKind.Variant => "Çeşitleme",
            _ => "Okunamayan yapı"
        };
    }

    private string TaskName(StepNode step)
    {
        string verb = step.Kind switch
        {
            StepKind.CreateRecord => "Kayıt oluştur",
            StepKind.UpdateRecord => "Kayıt güncelle",
            StepKind.AssignRecord => "Sahibini değiştir",
            StepKind.ChangeStatus => "Durum değiştir",
            StepKind.SendEmail => "E-posta gönder",
            StepKind.StartChildWorkflow => "Alt iş akışı başlat",
            StepKind.CustomActivity => "Özel etkinlik",
            StepKind.StopWorkflow => "Durdur",
            StepKind.Timeout => "Bekle",
            StepKind.UserInteraction => "Diyalog sayfası",
            StepKind.DataQuery => "Veri sorgula",
            StepKind.FormAction => step.Detail ?? "Form eylemi",
            StepKind.Unmapped => "OKUNAMADI " + step.Construct,
            _ => step.Kind.ToString()
        };
        string subject = Subject(step);
        return Truncate(subject.Length == 0 ? verb : $"{verb}: {subject}", 80);
    }

    /// <summary>
    /// What the step is about, for the label. The designer's own step name is used when the author wrote one; when
    /// they did not, CRM leaves only its internal id (<c>AssignStep3</c>), which tells a reader nothing, so the step
    /// is described by what it does instead. The internal id stays in the documentation either way.
    /// </summary>
    private string Subject(StepNode step)
    {
        if (step.DisplayName.Length > 0 && !InternalStepId().IsMatch(step.DisplayName))
        {
            return step.DisplayName;
        }
        List<string> parts = [];
        if (step.Entity is not null)
        {
            parts.Add(step.Entity);
        }
        if (step.Fields.Count > 0)
        {
            parts.Add(string.Join(", ", step.Fields.Take(3).Select(field => field.Field)) + (step.Fields.Count > 3 ? ", …" : ""));
        }
        if (parts.Count == 0 && step.Kind is StepKind.UserInteraction)
        {
            parts.AddRange(step.Arguments.Where(argument => argument.Name.EndsWith("PromptText", StringComparison.Ordinal)).Take(1).Select(argument => argument.Value));
        }
        if (parts.Count == 0 && step.Kind is StepKind.FormAction)
        {
            parts.AddRange(step.Arguments.Where(argument => argument.Name is "ControlId" or "Attribute" or "FieldName").Take(1).Select(argument => argument.Value));
        }
        if (step.Kind == StepKind.StartChildWorkflow && Guid.TryParse(step.Detail, out Guid child))
        {
            parts.Insert(0, _workflowNames.GetValueOrDefault(child, step.Detail));
        }
        if (parts.Count == 0 && step.Detail is not null && step.Kind is not (StepKind.FormAction or StepKind.StartChildWorkflow))
        {
            parts.Add(step.Detail);
        }
        return string.Join(" · ", parts);
    }

    private static string StartName(WorkflowTrigger trigger)
    {
        List<string> parts = [];
        if (trigger.OnCreate)
        {
            parts.Add("kayıt oluşturulunca");
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add("alan güncellenince");
        }
        if (trigger.OnDelete)
        {
            parts.Add("kayıt silinince");
        }
        if (trigger.OnDemand)
        {
            parts.Add("istek üzerine");
        }
        return parts.Count == 0 ? "Başlangıç" : string.Join(" / ", parts);
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
            parts.Add("kayıt oluşturuldu" + (trigger.CreateStage is null ? "" : $" ({trigger.CreateStage})"));
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add($"güncellenen alanlar: {string.Join(", ", trigger.OnUpdateFields)}" + (trigger.UpdateStage is null ? "" : $" ({trigger.UpdateStage})"));
        }
        if (trigger.OnDelete)
        {
            parts.Add("kayıt silindi" + (trigger.DeleteStage is null ? "" : $" ({trigger.DeleteStage})"));
        }
        return string.Join("; ", parts);
    }

    /// <summary>
    /// What a reader needs before reading the diagram: which CRM workflow this is, whether it can run at all, what
    /// starts it, how big it is, and how much of it the parser could not read. Kept short enough to take in at once.
    /// </summary>
    private static string HeaderNote(WorkflowIr ir, string name)
    {
        WorkflowIdentity identity = ir.Identity;
        int steps = CountSteps(ir.Steps);
        int unmapped = CountUnmapped(ir.Steps);
        List<string> lines =
        [
            name,
            $"{identity.Category} · {identity.Mode} · {identity.State} · varlık: {identity.PrimaryEntity ?? "yok"}",
            "Şununla başlar: " + (ir.Trigger.OnCreate || ir.Trigger.OnDelete || ir.Trigger.OnUpdateFields.Count > 0
                ? TriggerText(ir.Trigger)
                : ir.Trigger.OnDemand ? "bir kullanıcı başlatır (istek üzerine)" : "başka bir iş akışı çağırır"),
            $"{steps} adım" + (unmapped > 0 ? $"; {unmapped} tanesini ayrıştırıcı okuyamadı — aşağıda OKUNAMADI olarak işaretli" : ""),
            $"CRM iş akışı {identity.WorkflowId:D}",
            "CRM XAML dosyasından üretilmiş açıklayıcı modeldir. Çalıştırılabilir değildir; güvenmeden önce CRM ile karşılaştırın."
        ];
        if (identity.State == ProcessLabels.StateDraft)
        {
            lines.Insert(2, "CRM'de TASLAK: yeni çalıştırma başlatamaz.");
        }
        return string.Join("\n", lines);
    }

    private static int CountSteps(IReadOnlyList<StepNode> steps)
    {
        return steps.Sum(step => 1 + step.Branches.Sum(branch => CountSteps(branch.Steps)));
    }

    private static int CountUnmapped(IReadOnlyList<StepNode> steps)
    {
        return steps.Sum(step => (step.Kind == StepKind.Unmapped ? 1 : 0) + step.Branches.Sum(branch => CountUnmapped(branch.Steps)));
    }

    private static string TriggerDocumentation(WorkflowIr ir)
    {
        string entity = ir.Identity.PrimaryEntity ?? "none";
        string automatic = ir.Trigger.OnCreate || ir.Trigger.OnDelete || ir.Trigger.OnUpdateFields.Count > 0
            ? $"{entity} üzerinde şu durumda tetiklenir: {TriggerText(ir.Trigger)}."
            : "Otomatik tetikleyici yok.";
        string onDemand = ir.Trigger.OnDemand ? " Bir kullanıcı tarafından başlatılabilir (istek üzerine)." : "";
        return $"{automatic}{onDemand} Çalıştıran: {ir.Trigger.RunAs}.";
    }

    /// <summary>CRM's own step id when the author gave the step no name: <c>UpdateStep3</c>, <c>ConditionBranchStep12</c>.</summary>
    [GeneratedRegex(@"^[A-Za-z]+Step\d+$")]
    private static partial Regex InternalStepId();

    private static string Truncate(string text, int length)
    {
        string flat = text.ReplaceLineEndings(" ");
        return flat.Length <= length ? flat : flat[..(length - 1)] + "…";
    }
}
