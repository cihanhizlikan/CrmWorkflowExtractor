using System.Globalization;
using Crm.Cli.Stages;
using Crm.Ir.Model;
using Crm.Similarity;

namespace Crm.Cli.Reports;

/// <summary>
/// <c>reports/migration.csv</c>: one row per workflow, with everything an analyst needs to plan the rebuild in
/// another product — what it is, what it touches, who calls it, which family it belongs to, whether it is known to
/// run, and how much of it the parser could not read. Sorted so the work that matters most is at the top.
/// </summary>
public static class MigrationPlan
{
    public static Sheet Build(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity, UsageEvidence? usage)
    {
        Dictionary<Guid, WorkflowCluster> familyOf = [];
        foreach (WorkflowCluster cluster in similarity?.Clusters ?? [])
        {
            foreach (ClusterMember member in cluster.Members)
            {
                familyOf[member.WorkflowId] = cluster;
            }
        }
        CallGraph calls = CallGraph.Build(documents);
        (IReadOnlyDictionary<Guid, int> sharedFields, IReadOnlyDictionary<Guid, int> starts) = DataFootprint.PerWorkflow(documents);

        Sheet csv = new(SheetNames.Plan, "öncelik", "is_akisi", "bpmn_dosyasi", "kategori", "mod", "durum", "birincil_varlik", "tetikleyici",
            "adim", "okunamayan_adim", "ozel_etkinlikler", "cagirdigi", "cagiran", "rol",
            "aile", "aile_buyuklugu", "aile_rolu", "birlesik_dosya", "son_kayitli_calisma", "kullanim_hukmu",
            "adi_deneme_gibi", "urunle_gelen", "hassas_deger_var", "paylasilan_alan", "baslattigi_is_akisi", "yazdigi_varliklar", "yazdigi_alanlar");
        foreach (WorkflowIr document in documents.OrderBy(Priority).ThenBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            WorkflowIdentity identity = document.Identity;
            WorkflowCluster? family = familyOf.GetValueOrDefault(identity.WorkflowId);
            WorkflowUsage? found = usage?.Workflows.GetValueOrDefault(identity.WorkflowId);
            csv.Row(
                Priority(document),
                identity.Name,
                state.BpmnFiles.TryGetValue(identity.WorkflowId, out string? file) ? file + ".bpmn" : "",
                identity.Category,
                identity.Mode,
                identity.State,
                identity.PrimaryEntity,
                Trigger(document.Trigger),
                CountSteps(document.Steps),
                state.UnmappedSteps.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.Dependencies.CustomActivities.Select(ShortType)),
                document.Dependencies.ChildWorkflowCalls.Count,
                calls.CalledBy.GetValueOrDefault(identity.WorkflowId, []).Count,
                calls.RoleOf(identity.WorkflowId),
                family is null || family.Members.Count < 2 ? "" : family.ClusterId,
                family is null || family.Members.Count < 2 ? "" : family.Members.Count,
                family is null || family.Members.Count < 2 ? "" : family.Medoid == identity.WorkflowId ? "başlangıç noktası" : "üye",
                family is not null && state.CombinedFiles.TryGetValue(family.ClusterId, out string? combined) ? combined + ".bpmn" : "",
                found?.LastLoggedRun is DateTimeOffset last ? last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                UsageStage.Verdict(identity, usage),
                UsageStage.NameSuggestsTest(identity.Name),
                identity.IsManaged == true,
                state.SensitiveWorkflows.Contains(identity.WorkflowId),
                sharedFields.GetValueOrDefault(identity.WorkflowId),
                starts.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.DataTouched.EntitiesWritten),
                string.Join(" | ", document.DataTouched.FieldsWritten.Take(12)));
        }
        return csv;
    }

    /// <summary>
    /// Reading order, not importance: what is certainly live first, what cannot run last. Inside a band the
    /// analyst sorts the sheet however they like — that is why every fact is its own column.
    /// </summary>
    /// <summary>Priority band 1: a live process, the work the analysts start from.</summary>
    public static bool IsLiveProcess(WorkflowIr document)
    {
        return Priority(document) == 1;
    }

    private static int Priority(WorkflowIr document)
    {
        if (document.Identity.IsManaged == true)
        {
            return 5;
        }
        if (document.Identity.State == UsageStage.DraftState)
        {
            return 4;
        }
        if (UsageStage.NameSuggestsTest(document.Identity.Name))
        {
            return 3;
        }
        return document.Identity.Category is ProcessLabels.CategoryWorkflow or ProcessLabels.CategoryAction ? 1 : 2;
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
}
