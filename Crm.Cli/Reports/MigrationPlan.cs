using System.Globalization;
using Crm.Cli.Stages;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>
/// The migration plan and its counterpart, the out-of-scope list. One row per workflow, with everything an analyst
/// needs to plan the rebuild in another product — what it is, what it touches, who calls it, whether it is known
/// to run, and how much of it the parser could not read. What is not this company's to
/// rebuild goes into the second book instead, so nobody has to filter the first one to find the real work.
/// </summary>
public static class MigrationPlan
{
    public const string SuppliedReason = "ürünle gelmiş: yeni üründe sizin kurmanız gerekmez";
    public const string DraftReason = "taslak: çalıştırma başlatamaz";
    public const string TestNamedReason = "adı deneme gibi ve hiç kayıtlı çalışması yok";

    /// <summary>The work itself: everything that is this company's to rebuild.</summary>
    public static Sheet Build(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        Rows rows = new(state, documents, usage);
        // Column order is the order an analyst asks the questions in while redrawing a process: what is it, what
        // starts it — a record event, a case stage — how big is it, does it span time, does it stand alone, is it
        // alive, how much of the drawing can I trust, what does it drag along with it, and how does it run.
        Sheet sheet = new(SheetNames.Plan, "is_akisi", "kategori", "kayit_turu", "tetikleyici", "calistiran_asamalar",
            "adim_sayisi", "bekleme_var", "cagri_rolu", "cagiran_is_akislari", "cagirdigi_is_akislari",
            "kullanim", "son_kayitli_calisma", "okunamayan_adim_sayisi", "calisan_kopya_farkli",
            "ozel_etkinlikler", "tetikledigi_is_akisi_sayisi", "paylasilan_alan", "yazdigi_varliklar",
            "calisma_modu", "elle_baslatilabilir", "calisma_kimligi", "hassas_deger_var", "bpmn_dosyasi", "is_akisi_id");
        foreach (WorkflowIr document in InScope(state, documents, usage).OrderBy(Priority).ThenBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            rows.Plan(sheet, document);
        }
        return sheet;
    }

    /// <summary>
    /// The counterpart book: what the analysts should NOT spend a day on, and the one sentence saying why. It carries
    /// the same evidence as the plan, so a reader who doubts an exclusion can check it here instead of going to CRM.
    /// </summary>
    public static Sheet BuildExcluded(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        Sheet sheet = new(SheetNames.Excluded, "neden", "is_akisi", "kategori", "kayit_turu", "kullanim_hukmu",
            "son_kayitli_calisma", "adim_sayisi", "durum", "calisma_modu", "degistirilme", "bpmn_dosyasi", "is_akisi_id");
        foreach (WorkflowIr document in documents
            .Where(document => Reason(state, document.Identity, usage) is not null)
            .OrderBy(document => Reason(state, document.Identity, usage), StringComparer.Ordinal)
            .ThenBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            WorkflowIdentity identity = document.Identity;
            sheet.Row(Reason(state, identity, usage), identity.Name, identity.Category, identity.PrimaryEntity,
                UsageStage.Verdict(identity, usage), LastRun(usage, identity), CountSteps(document.Steps),
                identity.State, identity.Mode, identity.ModifiedOn,
                state.BpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "", identity.WorkflowId);
        }
        return sheet;
    }

    /// <summary>The workflows the plan covers: everything without a reason to leave it out.</summary>
    public static IEnumerable<WorkflowIr> InScope(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        return documents.Where(document => Reason(state, document.Identity, usage) is null);
    }

    /// <summary>
    /// Why this workflow is not the company's to rebuild, or null when it is. Only certainties: CRM says it came with
    /// the product, or a Draft definition cannot start a run. A name that reads like a test is a certainty only
    /// together with the absence of any logged run — workflows named DRAFT_* ran on the day the evidence was taken.
    /// An active stage firing a workflow outweighs all of it but a draft: the stage is the process, and a map that
    /// embeds a workflow the plan left out sends the reader to a diagram they were told not to need.
    /// </summary>
    public static string? Reason(RunState state, WorkflowIdentity identity, UsageEvidence? usage)
    {
        bool fired = state.FiredBy.ContainsKey(identity.WorkflowId);
        if (identity.IsManaged == true && !fired)
        {
            return SuppliedReason;
        }
        if (identity.State == UsageStage.DraftState)
        {
            return DraftReason;
        }
        if (fired)
        {
            return null;
        }
        bool everRan = usage?.Workflows.GetValueOrDefault(identity.WorkflowId)?.LastLoggedRun is not null;
        return UsageStage.NameSuggestsTest(identity.Name) && !everRan ? TestNamedReason : null;
    }

    /// <summary>Priority band 1: a live process, the work the analysts start from.</summary>
    public static bool IsLiveProcess(WorkflowIr document)
    {
        return Priority(document) == 1;
    }

    /// <summary>Reading order, not importance: a live process first, a dialog or business rule after it.</summary>
    private static int Priority(WorkflowIr document)
    {
        return document.Identity.Category is ProcessLabels.CategoryWorkflow or ProcessLabels.CategoryAction ? 1 : 2;
    }

    private static string LastRun(UsageEvidence? usage, WorkflowIdentity identity)
    {
        return usage?.Workflows.GetValueOrDefault(identity.WorkflowId)?.LastLoggedRun is DateTimeOffset last
            ? last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "";
    }

    /// <summary>
    /// Whether the process pauses: a timer or a wait-for-a-condition. It is one cell and it changes the whole shape
    /// of the redesign — a process that stays open for days is not the same object as one that runs to the end.
    /// </summary>
    private static bool Waits(IReadOnlyList<StepNode> steps)
    {
        return steps.Any(step => step.Kind is StepKind.Timeout or StepKind.WaitCondition || step.Branches.Any(branch => Waits(branch.Steps)));
    }

    internal static int CountSteps(IReadOnlyList<StepNode> steps)
    {
        return steps.Sum(step => 1 + step.Branches.Sum(branch => CountSteps(branch.Steps)));
    }

    private static string ShortType(string assemblyQualifiedName)
    {
        string type = assemblyQualifiedName.Split(',')[0];
        int dot = type.LastIndexOf('.');
        return dot < 0 ? type : type[(dot + 1)..];
    }

    private static string Trigger(WorkflowTrigger trigger)
    {
        List<string> parts = [];
        if (trigger.OnCreate)
        {
            parts.Add("kayıt oluşturulunca");
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add("şu alanlar güncellenince: " + string.Join(", ", trigger.OnUpdateFields.Take(6)));
        }
        if (trigger.OnDelete)
        {
            parts.Add("kayıt silinince");
        }
        if (trigger.OnDemand)
        {
            parts.Add("istek üzerine");
        }
        return parts.Count == 0 ? "kayıt olayı yok" : string.Join(" · ", parts);
    }

    public const string StageStep = "aşama adımı";

    /// <summary>The facts that take a pass over every workflow to work out, gathered once for the whole sheet.</summary>
    private sealed class Rows
    {
        private readonly RunState _state;
        private readonly UsageEvidence? _usage;
        private readonly CallGraph _calls;
        private readonly IReadOnlyDictionary<Guid, int> _sharedFields;
        private readonly IReadOnlyDictionary<Guid, int> _starts;
        private readonly HashSet<Guid> _drifted;

        public Rows(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
        {
            _state = state;
            _usage = usage;
            _calls = CallGraph.Build(documents);
            (_sharedFields, _starts) = DataFootprint.PerWorkflow(documents);
            _drifted = [.. (state.Drift?.Drifted ?? []).Where(finding => finding.StructureDiffers).Select(finding => finding.DefinitionId)];
        }

        /// <summary>
        /// How it is used: a step a case stage fires, a building block other workflows call, or an entry point that
        /// stands alone. A stage step is designed inside its stage map rather than as a process of its own.
        /// </summary>
        private string Role(Guid id, bool fired)
        {
            string calls = _calls.RoleOf(id);
            if (!fired)
            {
                return calls;
            }
            return calls == CallGraph.EntryPoint ? StageStep : StageStep + " + " + calls;
        }

        private string Name(Guid id)
        {
            return _calls.Names.GetValueOrDefault(id, "(envanterde bulunamadı)");
        }

        public void Plan(Sheet sheet, WorkflowIr document)
        {
            WorkflowIdentity identity = document.Identity;
            IReadOnlyList<StageFiring> fired = _state.FiredBy.GetValueOrDefault(identity.WorkflowId, []);
            sheet.Row(
                identity.Name,
                identity.Category,
                identity.PrimaryEntity,
                Trigger(document.Trigger),
                Sheet.List(fired.Select(firing => firing.Text)),
                CountSteps(document.Steps),
                Waits(document.Steps),
                Role(identity.WorkflowId, fired.Count > 0),
                Sheet.List(_calls.CalledBy.GetValueOrDefault(identity.WorkflowId, []).Select(Name).Order(StringComparer.Ordinal)),
                Sheet.List(_calls.Calls.GetValueOrDefault(identity.WorkflowId, []).Select(Name).Order(StringComparer.Ordinal)),
                UsageStage.ShortVerdict(identity, _usage),
                LastRun(_usage, identity),
                _state.UnmappedSteps.GetValueOrDefault(identity.WorkflowId),
                _state.Drift is null ? null : _drifted.Contains(identity.WorkflowId),
                Sheet.List(document.Dependencies.CustomActivities.Select(ShortType)),
                _starts.GetValueOrDefault(identity.WorkflowId),
                _sharedFields.GetValueOrDefault(identity.WorkflowId),
                Sheet.List(document.DataTouched.EntitiesWritten),
                identity.Mode,
                identity.IsOnDemand == true ? "evet" : "hayır — yalnızca CRM tetikler",
                document.Trigger.RunAs,
                _state.SensitiveWorkflows.Contains(identity.WorkflowId),
                _state.BpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "",
                identity.WorkflowId);
        }
    }
}
