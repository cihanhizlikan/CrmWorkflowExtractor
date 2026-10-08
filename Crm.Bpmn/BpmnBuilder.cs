using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crm.Bpmn.Graph;
using Crm.Ir.Model;

namespace Crm.Bpmn;

/// <summary>
/// What the rest of the run knows about a workflow and the diagram cannot work out for itself: what calls it,
/// which family it belongs to, whether it is known to run, and whether CRM's running copy differs from this
/// definition, and per custom activity type the addresses written inside the assembly that type comes from —
/// what the code behind a step reaches out to, which is the one thing no workflow record carries. Each is a
/// finished sentence for the header note; how to say it is the caller's business.
/// </summary>
public sealed record DiagramFacts(string? Role, string? Family, string? Usage, bool RunningCopyDiffers,
    IReadOnlyDictionary<string, string>? ActivityAddresses = null);

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
    private readonly IReadOnlyDictionary<string, string> _activityAddresses;

    private BpmnBuilder(string idBase, string workflowName, IReadOnlyDictionary<Guid, string>? memberNames,
        IReadOnlyDictionary<Guid, string>? workflowNames, DiagramFacts? facts)
    {
        _idBase = idBase;
        _workflowName = workflowName;
        _memberNames = memberNames ?? new Dictionary<Guid, string>();
        _workflowNames = workflowNames ?? new Dictionary<Guid, string>();
        _activityAddresses = facts?.ActivityAddresses ?? new Dictionary<string, string>();
    }

    public static string ProcessIdFor(Guid workflowId)
    {
        return "wf_" + workflowId.ToString("N");
    }

    public static BpmnProcess Build(WorkflowIr ir, IReadOnlyDictionary<Guid, string>? workflowNames = null, DiagramFacts? facts = null)
    {
        return Build(ProcessIdFor(ir.Identity.WorkflowId), ir.Identity.Name, ir, [new StepSource(ir.Identity.WorkflowId, "")], null, workflowNames, facts);
    }

    /// <summary>
    /// Builds from any IR; <paramref name="processId"/> also seeds every element id. For a combined workflow,
    /// <paramref name="memberNames"/> names each source workflow in the documentation.
    /// </summary>
    public static BpmnProcess Build(string processId, string name, WorkflowIr ir, IReadOnlyList<StepSource> sources,
        IReadOnlyDictionary<Guid, string>? memberNames = null, IReadOnlyDictionary<Guid, string>? workflowNames = null, DiagramFacts? facts = null)
    {
        BpmnBuilder builder = new(processId[(processId.IndexOf('_', StringComparison.Ordinal) + 1)..], name, memberNames, workflowNames, facts);
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
        builder._graph.FoldMergeGateways();

        // The note is a shape like any other, so the diagram starts below it and nothing is drawn over it.
        string note = HeaderNote(ir, name, facts);
        // A long line wraps inside the annotation, so the box is sized by the lines a reader will SEE, not by the
        // newlines in the text: underestimating would let the note spill out of its own border.
        int visualLines = note.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / 80.0)));
        double noteHeight = (visualLines * 15) + 16;
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
            StepKind.Condition => Split(step, FlowNodeType.ExclusiveGateway),
            StepKind.Variant => Split(step, FlowNodeType.ExclusiveGateway),
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

    /// <summary>
    /// A split and its branches. Which arrow is the DEFAULT is decided by a branch having no condition on it, not
    /// by matching its caption: the caption looked for was "Aksi hâlde" and the parser writes "Otherwise", so an
    /// explicit else was never recognised — it lost its default marker AND was given a second, unreachable
    /// "nothing matched" arrow beside it, on every condition in the estate that had one.
    /// </summary>
    private SplitBlock Split(StepNode step, FlowNodeType gatewayType)
    {
        // A CONDITION IS NEVER SHORTENED. Everything else on a diagram can be cut back to what fits, but the
        // test a process turns on is the reason a reader opened it, and "(phonecall.ps_activitysubresultid =
        // Satış Yapıldı) VE …" hides the half that decides. The cap that exists elsewhere is there because an
        // address list reached thirty thousand characters on one label; a condition is bounded by the fields it
        // names and cannot run away like that.
        FlowNode split = _graph.Add(new FlowNode(Id(step.Path, "split"), gatewayType, GatewayName(step))
        {
            Documentation = StepDocumentation(step),
            Sources = step.Sources
        });
        FlowNode join = new(Id(step.Path, "join"), FlowNodeType.ExclusiveGateway, "");
        List<SplitPath> paths = [];
        bool asksTheComparison = AsksTheComparison(step);
        string? shared = SharedSubject(step);
        foreach (Branch branch in step.Branches)
        {
            bool isDefault = step.Kind == StepKind.Condition && branch.Predicate is null;
            paths.Add(new SplitPath(BranchLabel(step, branch, asksTheComparison, shared, isDefault),
                branch.Predicate?.Text ?? branch.Label, isDefault, Sequence(branch.Steps)));
        }
        if (step.Kind == StepKind.Condition && !step.Branches.Any(branch => branch.Predicate is null))
        {
            // A condition with no otherwise-branch carries on when none held. That is an OUTCOME, so the arrow
            // says what happened — "hayır", or "diğer" where there were several to fail — and not the machinery
            // that produced it. "(hiçbir koşul sağlanmazsa)" described how this tool built the diagram, which is
            // not something a reader of a process has any use for.
            paths.Add(new SplitPath(Fallthrough(step), null, true, new SequenceBlock(_graph, [])));
        }
        return new SplitBlock(_graph, split, join, paths);
    }

    /// <summary>
    /// What one outgoing arrow says. Its job is to carry the OUTCOME, and how much of the comparison it needs for
    /// that depends on what the diamond already said: nothing, when the diamond carries the whole comparison and
    /// the arrow is simply yes or no; the operator and value alone, when every branch tests the same field and the
    /// diamond names it; and the whole condition otherwise.
    /// </summary>
    private static string BranchLabel(StepNode step, Branch branch, bool asksTheComparison, string? shared, bool isDefault)
    {
        if (step.Kind == StepKind.Variant)
        {
            return "Çeşitleme: " + branch.Label;
        }
        if (branch.Predicate is not Predicate predicate)
        {
            return isDefault || branch.Label.Length == 0 ? Fallthrough(step) : branch.Label;
        }

        if (asksTheComparison)
        {
            return "evet";
        }
        if (shared is not null && predicate.Text.StartsWith(shared + " ", StringComparison.Ordinal))
        {
            return predicate.Text[(shared.Length + 1)..];
        }
        return predicate.Text;
    }

    /// <summary>The arrow taken when nothing held: "no" where there was one test, "other" where there were several.</summary>
    private static string Fallthrough(StepNode step)
    {
        return step.Branches.Count(branch => branch.Predicate is not null) > 1 ? "diğer" : "hayır";
    }

    /// <summary>
    /// The field every branch of this split tests, when they all test the same one. Then the diamond can name it
    /// once and each arrow carry only its own operator and value — <c>lead.prioritycode?</c> with "Equal Düşük
    /// (2)" beside it, rather than that field's name repeated on every arrow leaving it.
    /// </summary>
    private static string? SharedSubject(StepNode step)
    {
        List<Predicate> predicates = [.. step.Branches.Select(branch => branch.Predicate).OfType<Predicate>()];
        if (predicates.Count < 2)
        {
            return null;
        }
        string first = Asked(predicates[0]);
        return predicates.All(predicate => Asked(predicate) == first) ? first : null;
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
        if (step.Branches.Select(branch => branch.Predicate).FirstOrDefault(predicate => predicate is not null) is Predicate first)
        {
            return AsksTheComparison(step) ? first.Text : Asked(first) + "?";
        }
        return step.Kind == StepKind.Variant ? "Üyeler ayrışıyor" : "Koşul";
    }

    /// <summary>
    /// Whether the diamond should carry the whole comparison rather than name what is compared.
    ///
    /// <para>
    /// It should wherever there is ONE test and the author did not name the step. <c>lead.leadid?</c> names the
    /// field and leaves the reader asking what is being asked ABOUT it — is it a null check, a match, a range? —
    /// while the answer, <c>lead.leadid NotNull</c>, sat on the arrow where it read as a repetition. The diamond
    /// takes the whole comparison and the arrows become yes and no, which is what a reader of a decision expects
    /// to find on them. With SEVERAL tests this cannot work: each arrow has its own, so the diamond names the
    /// field they share and the arrows carry the rest. An author's own name for the step always wins over both.
    /// </para>
    /// </summary>
    private static bool AsksTheComparison(StepNode step)
    {
        return step.Kind == StepKind.Condition
            && (step.DisplayName.Length == 0 || InternalStepId().IsMatch(step.DisplayName))
            && step.Branches.Count(branch => branch.Predicate is not null) == 1;
    }

    /// <summary>
    /// What the diamond asks about. The field when the definition named one, and otherwise whatever the condition
    /// compares, taken from the text up to the operator: the designer's own variable is a poor name but a real
    /// one, and the diamond used to say "Koşul" — the word "condition" — which asks nothing at all.
    /// </summary>
    private static string Asked(Predicate predicate)
    {
        if (predicate is { Entity: string entity, Attribute: string attribute })
        {
            return $"{entity}.{attribute}";
        }
        return predicate.Subject.Length > 0 ? predicate.Subject : predicate.Text;
    }

    /// <summary>A single wait is one conditional catch event; a wait with several outcomes (e.g. a timeout) is an event-based gateway.</summary>
    private Block Wait(StepNode step)
    {
        if (step.Branches.Count <= 1)
        {
            Branch? only = step.Branches.FirstOrDefault();
            FlowNode waitEvent = _graph.Add(new FlowNode(Id(step.Path), FlowNodeType.ConditionalCatchEvent, "Bekle: " + (only?.Label ?? step.DisplayName))
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

        FlowNode gateway = _graph.Add(new FlowNode(Id(step.Path, "split"), FlowNodeType.EventBasedGateway, step.DisplayName)
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
                timer ? FlowNodeType.TimerCatchEvent : FlowNodeType.ConditionalCatchEvent, branch.Label)
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
            text.Append(" Yazar: ").Append(field.Field).Append(" = ").Append(Values(field)).Append('.');
        }
        if (step.Detail is not null)
        {
            text.Append(" Ayrıntı: ").Append(step.Detail).Append('.');
        }
        foreach (NamedArgument argument in step.Arguments)
        {
            text.Append(argument.Output switch { true => " Çıktı: ", false => " Girdi: ", _ => " Bağımsız değişken: " })
                .Append(argument.Name).Append(" = ").Append(argument.Value).Append('.');
        }
        foreach (Branch branch in step.Branches.Where(branch => branch.Predicate is not null))
        {
            // The full condition, which the diamond has no room for and the branch's own arrow shows only once.
            text.Append(" Dal '").Append(branch.Label).Append("': ").Append(branch.Predicate!.Text).Append('.');
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
        // Not shortened to fit: the box is grown to hold it instead. What a step writes is the substance of the
        // step, and "customer, prioritycode, ps_campaignresponseresultid, …" threw away seven of the ten fields
        // that make it what it is. MaxLabel stays as a guard against a definition nobody has seen yet, far above
        // anything the estate contains — it is not a style rule and no real label comes near it.
        return Truncate((subject.Length == 0 ? verb : $"{verb}: {subject}") + Target(step, subject), MaxLabel);
    }

    /// <summary>
    /// Where the step hands over to: the workflow a call activity starts, or the registered code a custom activity
    /// runs. The author's own label rarely names either, and both are what a reader needs to follow the thread.
    /// </summary>
    private string Target(StepNode step, string subject)
    {
        if (step.Kind == StepKind.StartChildWorkflow && Guid.TryParse(step.Detail, out Guid child)
            && _workflowNames.GetValueOrDefault(child) is string target && !subject.Contains(target, StringComparison.Ordinal))
        {
            return " → " + target;
        }
        if (step.Kind == StepKind.CustomActivity && step.Detail is string type)
        {
            string full = type.Split(',')[0].Trim();
            string name = full[(full.LastIndexOf('.') + 1)..];
            string code = name.Length == 0 || subject.Contains(name, StringComparison.Ordinal) ? "" : $" ({name})";
            return _activityAddresses.TryGetValue(full, out string? host) ? code + " → " + host : code;
        }
        return "";
    }

    /// <summary>The value(s) a field is given, as a reader should see them: the option's label with its number behind it.</summary>
    private static string Values(FieldWrite field)
    {
        return string.Join(" | ", field.Values.Select(value => value.Resolved is null ? value.Raw : $"{value.Resolved} ({value.Raw})"));
    }

    /// <summary>
    /// A field on the box, and the value it is given where the DEFINITION FIXES IT.
    ///
    /// <para>
    /// A value CRM works out at run time is not one, and the field is then named on its own: "= &lt;dynamic&gt;"
    /// costs a line of the box and tells a reader nothing they could not see. Which fields a step writes was all
    /// the box used to say, and for the static ones that left the substance of the step — what it actually sets
    /// them TO — readable only in the documentation (maintainer, 2026-10-08). A field with several values must
    /// have all of them fixed to be shown: half a list is worse than none.
    /// </para>
    /// </summary>
    private static string Written(FieldWrite field)
    {
        if (field.Values.Count == 0 || field.Values.Any(value => value.Raw == LiteralValue.Dynamic))
        {
            return field.Field;
        }
        return field.Field + " = " + Values(field);
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
            parts.Add(string.Join(", ", step.Fields.Select(Written)));
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
        if (parts.Count == 0 && step.Kind == StepKind.CustomActivity && step.Detail is string activity)
        {
            parts.Add(Signature(activity, step.Arguments));
        }
        if (parts.Count == 0 && step.Detail is not null && step.Kind is not (StepKind.FormAction or StepKind.StartChildWorkflow))
        {
            parts.Add(step.Detail);
        }
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// A custom activity as a signature: what it is called with, and what it hands back. The label used to carry
    /// the assembly-qualified type — <c>GNB_Workflow.Contact_CheckRetirementEligibilityByNova, GNB_Workflow,
    /// Version=1.0.0.0, Culture=neutral, PublicKeyToken=…</c> — which filled the box, was cut off mid-word, and
    /// told a reader only where the code lives. What it DOES is compiled and unreadable from here, but the names
    /// it is called with and writes back are in the definition, and they are the nearest thing to a description
    /// of it that exists: ContactId in, CanProceed and WarningMessage out.
    /// </summary>
    private static string Signature(string activityType, IReadOnlyList<NamedArgument> arguments)
    {
        string full = activityType.Split(',')[0].Trim();
        string name = full[(full.LastIndexOf('.') + 1)..];
        string takes = string.Join(", ", arguments.Where(argument => argument.Output != true).Select(argument => argument.Name));
        string gives = string.Join(", ", arguments.Where(argument => argument.Output == true).Select(argument => argument.Name));
        string called = name + "(" + takes + ")";
        return gives.Length == 0 ? called : called + " → " + gives;
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
    /// <summary>
    /// What a reader has to know before they trust the picture, on the picture itself. Everything here answers a
    /// question an analyst would otherwise have to leave the file to answer: is this one of many like it, does
    /// anything else call it, is it alive, and is what runs in CRM actually what is drawn here.
    /// </summary>
    private static string HeaderNote(WorkflowIr ir, string name, DiagramFacts? facts)
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
            $"{steps} adım" + (unmapped > 0 ? $"; {unmapped} tanesini ayrıştırıcı okuyamadı — aşağıda OKUNAMADI olarak işaretli" : "")
        ];
        if (ir.Parameters.Count > 0)
        {
            // For an ACTION this is the contract: what a caller passes and what comes back. Nothing else outside
            // the compiled code describes it, and a one-step diagram of a single custom activity has nothing else
            // on it at all.
            string takes = string.Join(", ", ir.Parameters.Where(parameter => !parameter.Output).Select(parameter => $"{parameter.Name} ({parameter.Type})"));
            string gives = string.Join(", ", ir.Parameters.Where(parameter => parameter.Output).Select(parameter => $"{parameter.Name} ({parameter.Type})"));
            lines.Add("Parametreler: " + (takes.Length == 0 ? "girdi yok" : "girdi " + takes)
                + (gives.Length == 0 ? "" : " · çıktı " + gives));
        }
        if (facts?.Role is string role)
        {
            lines.Add("Rol: " + role);
        }
        if (facts?.Family is string family)
        {
            lines.Add("Aile: " + family);
        }
        if (facts?.Usage is string usage)
        {
            lines.Add("Kullanım: " + usage);
        }
        if (facts?.ActivityAddresses is { Count: > 0 } addresses)
        {
            lines.Add("Dış çağrı: " + string.Join(" · ", addresses.Select(entry => entry.Key[(entry.Key.LastIndexOf('.') + 1)..] + " → " + entry.Value))
                + ". Bunlar, adımın çalıştırdığı kodun içinde yazılı adreslerdir; adımın oraya gittiğinin kanıtı değildir.");
        }
        if (facts?.RunningCopyDiffers == true)
        {
            lines.Add("UYARI: CRM'de çalışan kopya bu tanımdan farklı. Üretimde çalışan, burada çizilen olmayabilir.");
        }
        if (identity.IsManaged == true)
        {
            lines.Add("Ürünle gelmiş (yönetilen çözüm): yeni üründe bunu sizin kurmanız gerekmez.");
        }
        lines.Add($"CRM iş akışı {identity.WorkflowId:D}");
        lines.Add("CRM XAML dosyasından üretilmiş açıklayıcı modeldir. Çalıştırılabilir değildir; güvenmeden önce CRM ile karşılaştırın.");
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

    /// <summary>The designer's own generated condition variable, e.g. <c>ConditionBranchStep12_1</c>: a name that names nothing.</summary>
    [GeneratedRegex(@"^[A-Za-z]+Step\d+_[A-Za-z0-9_]+$")]
    private static partial Regex DesignerVariable();

    /// <summary>
    /// A guard, not a style rule. One label once reached thirty thousand characters — an assembly's whole address
    /// list, poured into it — and no modeller would open the file. Everything that fed that is bounded at its
    /// source now, and no label the estate produces comes within an order of magnitude of this; it stands only so
    /// that a definition nobody has seen yet cannot make a file nobody can open.
    /// </summary>
    public const int MaxLabel = 2000;

    private static string Truncate(string text, int length)
    {
        string flat = text.ReplaceLineEndings(" ");
        return flat.Length <= length ? flat : flat[..(length - 1)] + "…";
    }
}
