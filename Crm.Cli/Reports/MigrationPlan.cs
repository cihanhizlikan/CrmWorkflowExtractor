using System.Globalization;
using System.Text;
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
    public static byte[] Csv(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity, UsageEvidence? usage)
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

        ExcelCsv csv = new("priority", "workflow_name", "bpmn_file", "category", "mode", "state", "primary_entity", "trigger",
            "steps", "unmapped_steps", "custom_activities", "calls", "called_by", "role",
            "family", "family_size", "family_role", "combined_file", "last_logged_run", "usage_verdict",
            "name_suggests_test", "has_sensitive_literals", "shared_fields_written", "starts_other_workflows", "entities_written", "fields_written");
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
                family is null || family.Members.Count < 2 ? "" : family.Medoid == identity.WorkflowId ? "starting point" : "member",
                family is not null && state.CombinedFiles.TryGetValue(family.ClusterId, out string? combined) ? combined + ".bpmn" : "",
                found?.LastLoggedRun is DateTimeOffset last ? last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                UsageStage.Verdict(identity, usage),
                UsageStage.NameSuggestsTest(identity.Name),
                state.SensitiveWorkflows.Contains(identity.WorkflowId),
                sharedFields.GetValueOrDefault(identity.WorkflowId),
                starts.GetValueOrDefault(identity.WorkflowId),
                string.Join(" | ", document.DataTouched.EntitiesWritten),
                string.Join(" | ", document.DataTouched.FieldsWritten.Take(12)));
        }
        return csv.ToBytes();
    }

    /// <summary>
    /// Reading order, not importance: what is certainly live first, what cannot run last. Inside a band the
    /// analyst sorts the sheet however they like — that is why every fact is its own column.
    /// </summary>
    private static int Priority(WorkflowIr document)
    {
        if (document.Identity.State == UsageStage.DraftState)
        {
            return 4;
        }
        if (UsageStage.NameSuggestsTest(document.Identity.Name))
        {
            return 3;
        }
        return document.Identity.Category is "Workflow" or "Action" ? 1 : 2;
    }

    public static string Markdown(RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult? similarity)
    {
        CallGraph calls = CallGraph.Build(documents);
        StringBuilder text = new();
        text.AppendLine("# Migration worksheet").AppendLine();
        text.AppendLine("`migration.csv` has one row per workflow. Open it in Excel and sort or filter; every fact is a column, so the sheet answers whatever question comes up rather than fixing one order.").AppendLine();
        text.AppendLine("| Column | What it is for |").AppendLine("|---|---|");
        text.AppendLine("| `priority` | 1 live process · 2 dialog, rule or process flow · 3 name reads like a test · 4 Draft, cannot run |");
        text.AppendLine("| `bpmn_file` | the diagram, under `bpmn/<category>/<entity>/` |");
        text.AppendLine("| `trigger` | what starts it: create, update of named fields, delete, on demand |");
        text.AppendLine("| `steps` / `unmapped_steps` | size, and how much of it the parser could not read (check those by hand) |");
        text.AppendLine("| `custom_activities` | partner or in-house code the new product has no equivalent for |");
        text.AppendLine("| `calls` / `called_by` / `role` | the call graph: an entry point is migrated whole, a building block is shared |");
        text.AppendLine("| `family` / `family_role` / `combined_file` | near-duplicates, and the combined model of the family |");
        text.AppendLine("| `last_logged_run` / `usage_verdict` | evidence of use. Absence never proves non-use |");
        text.AppendLine("| `has_sensitive_literals` | its XAML holds a URL, user name or secret; see the restricted report |");
        text.AppendLine("| `shared_fields_written` | fields it writes that another workflow writes too — see `data-footprint.md` |");
        text.AppendLine("| `starts_other_workflows` | workflows its writes set off, without any explicit call — see `data-cascades.csv` |");
        text.AppendLine("| `entities_written` / `fields_written` | the data footprint — two workflows writing one field need care in the new product |");
        text.AppendLine();

        int live = documents.Count(document => Priority(document) == 1);
        int blocks = calls.CalledBy.Count(entry => entry.Value.Count > 0);
        text.AppendLine(CultureInfo.InvariantCulture, $"**{documents.Count} workflows.** {live} are live processes (priority 1); {documents.Count(document => Priority(document) == 4)} are Draft and cannot run. {blocks} are called by another workflow, so they are building blocks rather than separate work.").AppendLine();
        int families = similarity?.Clusters.Count(cluster => cluster.Members.Count > 1) ?? 0;
        int inFamilies = similarity?.Clusters.Where(cluster => cluster.Members.Count > 1).Sum(cluster => cluster.Members.Count) ?? 0;
        text.AppendLine(CultureInfo.InvariantCulture, $"{inFamilies} workflows fall into {families} families of near-duplicates. Start from each family's starting point and treat the rest as variations, not as separate builds.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{state.SensitiveWorkflows.Count} workflows hold a sensitive literal. Their diagrams carry those values too — treat `bpmn/` as production data.");
        return text.ToString();
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
            parts.Add("create");
        }
        if (trigger.OnUpdateFields.Count > 0)
        {
            parts.Add("update of " + string.Join(", ", trigger.OnUpdateFields.Take(6)));
        }
        if (trigger.OnDelete)
        {
            parts.Add("delete");
        }
        if (trigger.OnDemand)
        {
            parts.Add("on demand");
        }
        return parts.Count == 0 ? "child process only" : string.Join(" · ", parts);
    }
}
