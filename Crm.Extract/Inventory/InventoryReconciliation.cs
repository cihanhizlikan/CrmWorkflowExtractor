using System.Globalization;

namespace Crm.Extract.Inventory;

/// <summary>Counts by classification, the start of the §8 count chain.</summary>
public sealed record InventoryCounts(
    int ApiCount,
    int Retrieved,
    int DistinctWorkflowIds,
    int Definitions,
    int Activations,
    int Templates,
    int OtherType,
    int DefinitionsNotDesignerAuthored,
    int DistinctOwners);

public sealed record ReconciliationResult(InventoryCounts Counts, IReadOnlyList<string> Failures, IReadOnlyList<string> Warnings)
{
    public bool Passed
    {
        get { return Failures.Count == 0; }
    }
}

/// <summary>
/// §2.4 / §8 reconciliation over the inventory. A failure here fails the run loudly; a warning is printed
/// prominently and recorded. Note what this can and cannot see: it catches paging and duplication faults, and
/// the single-owner signature — insufficient read depth is <see cref="Preflight.PrivilegeCheck"/>'s job.
/// </summary>
public static class InventoryReconciliation
{
    /// <summary>The Web API caps <c>$count</c> at 5000; at or above it the number is no longer a count.</summary>
    public const int CountCap = 5000;

    public static ReconciliationResult Evaluate(int apiCount, IReadOnlyList<WorkflowInventoryRecord> records)
    {
        List<string> failures = [];
        List<string> warnings = [];

        int distinctIds = records.Select(record => record.WorkflowId).Distinct().Count();
        int distinctOwners = records.Where(record => record.OwnerId is not null).Select(record => record.OwnerId).Distinct().Count();
        List<WorkflowInventoryRecord> definitions = [.. records.Where(record => record.Type.Raw == WorkflowOptionSets.TypeDefinition)];
        List<WorkflowInventoryRecord> activations = [.. records.Where(record => record.Type.Raw == WorkflowOptionSets.TypeActivation)];
        int templates = records.Count(record => record.Type.Raw == WorkflowOptionSets.TypeTemplate);
        int other = records.Count - definitions.Count - activations.Count - templates;
        int notDesigner = definitions.Count(record => record.IsCrmUiWorkflow == false);

        InventoryCounts counts = new(apiCount, records.Count, distinctIds, definitions.Count, activations.Count, templates, other, notDesigner, distinctOwners);

        if (apiCount < 0)
        {
            // Observed on the production 8.2 server, 2026-09-22: workflows/$count answers -1, Dynamics' "no count
            // available". That is not a number to compare, and failing on it would fail every run on that server.
            warnings.Add(Invariant($"The server gave no independent count (it answered {apiCount}), so the {records.Count} records retrieved could not be checked against one. Compare with an administrator's count before trusting completeness."));
        }
        else if (apiCount >= CountCap)
        {
            failures.Add(Invariant($"$count returned {apiCount}, at or above the Web API cap of {CountCap}: it is not a trustworthy count."));
        }
        else if (apiCount != records.Count)
        {
            failures.Add(Invariant($"$count reported {apiCount} workflows but {records.Count} were retrieved."));
        }
        if (distinctIds != records.Count)
        {
            failures.Add(Invariant($"{records.Count} records were retrieved but only {distinctIds} distinct workflowid values: paging returned duplicates."));
        }
        if (other > 0)
        {
            warnings.Add(Invariant($"{other} record(s) have a type outside Definition/Activation/Template."));
        }
        if (records.Count > 0 && distinctOwners == 1)
        {
            warnings.Add("ONLY ONE DISTINCT OWNER across every workflow. This is the signature of user-level read privilege (§2.4): "
                + "the account may be seeing only its own workflows. Do not trust this run until an administrator's count matches.");
        }
        if (records.Count > 0 && distinctOwners == 0)
        {
            warnings.Add("No record carries _ownerid_value, so the owner-count check could not run.");
        }
        if (records.Count == 0)
        {
            warnings.Add("The organization returned zero workflows.");
        }

        HashSet<Guid> definitionIds = [.. definitions.Select(record => record.WorkflowId)];
        HashSet<Guid> activationIds = [.. activations.Select(record => record.WorkflowId)];
        int orphanActivations = activations.Count(record => record.ParentWorkflowId is not Guid parent || !definitionIds.Contains(parent));
        if (orphanActivations > 0)
        {
            warnings.Add(Invariant($"{orphanActivations} activation record(s) point at no retrieved definition through _parentworkflowid_value."));
        }
        int danglingActive = definitions.Count(record => record.ActiveWorkflowId is Guid active && !activationIds.Contains(active));
        if (danglingActive > 0)
        {
            warnings.Add(Invariant($"{danglingActive} definition(s) point at an activation through _activeworkflowid_value that was not retrieved."));
        }

        foreach (IGrouping<string, ObservedOption> unknown in records
            .SelectMany(record => record.ObservedOptions)
            .Where(option => !WorkflowOptionSets.Describe(option.Column, option.Raw).IsKnown)
            .GroupBy(option => Invariant($"{option.Column}={option.Raw} (server label '{option.ServerLabel ?? "none"}')"), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            warnings.Add(Invariant($"Option value outside the §3.1 table: {unknown.Key}, on {unknown.Count()} record(s)."));
        }

        return new ReconciliationResult(counts, failures, warnings);
    }

    private static string Invariant(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }
}
