using System.Globalization;
using Crm.Cli.Stages;
using Crm.Ir.Model;
using Crm.Similarity;

namespace Crm.Cli.Reports;

/// <summary>
/// The migration plan and its counterpart, the out-of-scope list. One row per workflow, with everything an analyst
/// needs to plan the rebuild in another product — what it is, what it touches, who calls it, which family it belongs
/// to, whether it is known to run, and how much of it the parser could not read. What is not this company's to
/// rebuild goes into the second book instead, so nobody has to filter the first one to find the real work.
/// </summary>
public static class MigrationPlan
{
    private const string SuppliedReason = "ürünle gelmiş: yeni üründe sizin kurmanız gerekmez";
    private const string DraftReason = "taslak: çalıştırma başlatamaz";
    private const string TestNamedReason = "adı deneme gibi ve hiç kayıtlı çalışması yok";

    /// <summary>The work itself: everything that is this company's to rebuild.</summary>
    public static Sheet Build(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity, UsageEvidence? usage)
    {
        Rows rows = new(state, documents, similarity, usage);
        // Column order is the order an analyst asks the questions in: what is it, what starts it, how big, does it
        // stand alone, is it a duplicate, is it alive, how much of it can I trust, what does it drag along.
        Sheet sheet = new(SheetNames.Plan, "öncelik", "is_akisi", "kategori", "birincil_varlik", "tetikleyici", "adim", "rol",
            "aile", "aile_rolu", "aile_buyuklugu", "kullanim_hukmu", "son_kayitli_calisma", "okunamayan_adim",
            "ozel_etkinlikler", "baslattigi_is_akisi", "paylasilan_alan", "yazdigi_varliklar", "yazdigi_alanlar",
            "mod", "durum", "hassas_deger_var", "bpmn_dosyasi", "birlesik_dosya", "is_akisi_id");
        foreach (WorkflowIr document in InScope(documents, usage).OrderBy(Priority).ThenBy(document => document.Identity.Name, StringComparer.Ordinal))
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
        Sheet sheet = new(SheetNames.Excluded, "neden", "is_akisi", "kategori", "birincil_varlik", "kullanim_hukmu",
            "son_kayitli_calisma", "adim", "durum", "mod", "degistirilme", "bpmn_dosyasi", "is_akisi_id");
        foreach (WorkflowIr document in documents
            .Where(document => Reason(document.Identity, usage) is not null)
            .OrderBy(document => Reason(document.Identity, usage), StringComparer.Ordinal)
            .ThenBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            WorkflowIdentity identity = document.Identity;
            sheet.Row(Reason(identity, usage), identity.Name, identity.Category, identity.PrimaryEntity,
                UsageStage.Verdict(identity, usage), LastRun(usage, identity), CountSteps(document.Steps),
                identity.State, identity.Mode, identity.ModifiedOn,
                state.BpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "", identity.WorkflowId);
        }
        return sheet;
    }

    /// <summary>The workflows the plan covers: everything without a reason to leave it out.</summary>
    public static IEnumerable<WorkflowIr> InScope(IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        return documents.Where(document => Reason(document.Identity, usage) is null);
    }

    /// <summary>
    /// Why this workflow is not the company's to rebuild, or null when it is. Only certainties: CRM says it came with
    /// the product, or a Draft definition cannot start a run. A name that reads like a test is a certainty only
    /// together with the absence of any logged run — workflows named DRAFT_* ran on the day the evidence was taken.
    /// </summary>
    public static string? Reason(WorkflowIdentity identity, UsageEvidence? usage)
    {
        if (identity.IsManaged == true)
        {
            return SuppliedReason;
        }
        if (identity.State == UsageStage.DraftState)
        {
            return DraftReason;
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

    private static int CountSteps(IReadOnlyList<StepNode> steps)
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
        return parts.Count == 0 ? "yalnızca alt süreç olarak" : string.Join(" · ", parts);
    }

    /// <summary>The facts that take a pass over every workflow to work out, gathered once for the whole sheet.</summary>
    private sealed class Rows
    {
        private readonly RunState _state;
        private readonly UsageEvidence? _usage;
        private readonly CallGraph _calls;
        private readonly Dictionary<Guid, WorkflowCluster> _familyOf = [];
        private readonly Dictionary<Guid, string> _names;
        private readonly IReadOnlyDictionary<Guid, int> _sharedFields;
        private readonly IReadOnlyDictionary<Guid, int> _starts;

        public Rows(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity, UsageEvidence? usage)
        {
            _state = state;
            _usage = usage;
            _calls = CallGraph.Build(documents);
            foreach (WorkflowCluster cluster in similarity?.Clusters ?? [])
            {
                foreach (ClusterMember member in cluster.Members)
                {
                    _familyOf[member.WorkflowId] = cluster;
                }
            }
            // A family is named after the workflow the analysts start from, not after its internal cluster id.
            _names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
            (_sharedFields, _starts) = DataFootprint.PerWorkflow(documents);
        }

        public void Plan(Sheet sheet, WorkflowIr document)
        {
            WorkflowIdentity identity = document.Identity;
            WorkflowCluster? family = _familyOf.GetValueOrDefault(identity.WorkflowId);
            bool inFamily = family is not null && family.Members.Count > 1;
            sheet.Row(
                Priority(document),
                identity.Name,
                identity.Category,
                identity.PrimaryEntity,
                Trigger(document.Trigger),
                CountSteps(document.Steps),
                _calls.RoleOf(identity.WorkflowId),
                inFamily ? _names.GetValueOrDefault(family!.Medoid, family.ClusterId) : "",
                inFamily ? family!.Medoid == identity.WorkflowId ? "başlangıç noktası" : "üye" : "",
                inFamily ? family!.Members.Count : null,
                UsageStage.Verdict(identity, _usage),
                LastRun(_usage, identity),
                _state.UnmappedSteps.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.Dependencies.CustomActivities.Select(ShortType)),
                _starts.GetValueOrDefault(identity.WorkflowId),
                _sharedFields.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.DataTouched.EntitiesWritten),
                string.Join(" | ", document.DataTouched.FieldsWritten.Take(12)),
                identity.Mode,
                identity.State,
                _state.SensitiveWorkflows.Contains(identity.WorkflowId),
                _state.BpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "",
                inFamily && _state.CombinedFiles.TryGetValue(family!.ClusterId, out string? combined) ? combined + ".bpmn" : "",
                identity.WorkflowId);
        }
    }
}
