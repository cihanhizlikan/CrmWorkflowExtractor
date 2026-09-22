using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Consolidation;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Similarity;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// M5b: every cohesive family becomes <c>consolidated/&lt;clusterid&gt;.json</c> (combined IR) and
/// <c>consolidated/&lt;clusterid&gt;.bpmn</c>, with <c>reports/consolidation.md</c> saying what was combined, what was
/// not and why. The per-workflow BPMN files stay beside them as the originals to check the combination against.
/// </summary>
public static class ConsolidationStage
{
    public static async Task RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityResult similarity, ILogger logger, CancellationToken token)
    {
        Dictionary<Guid, WorkflowIr> byId = documents.ToDictionary(document => document.Identity.WorkflowId);
        Dictionary<Guid, string> allNames = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        Dictionary<string, string> fileNames = new(StringComparer.Ordinal);
        List<CombineOutcome> outcomes = [];
        foreach (WorkflowCluster cluster in similarity.Clusters.Where(cluster => cluster.Members.Count > 1))
        {
            CombineOutcome outcome = WorkflowCombiner.Combine(cluster, byId);
            outcomes.Add(outcome);
            if (outcome.Combined is not WorkflowIr combined)
            {
                continue;
            }
            foreach (string error in outcome.ReconciliationErrors)
            {
                state.Fail(ExitCode.RunFailed, $"{cluster.ClusterId}: {error}");
            }

            Dictionary<Guid, string> names = outcome.Members.ToDictionary(id => id, id => byId[id].Identity.Name);
            List<StepSource> sources = [.. outcome.Members.Select(id => new StepSource(id, ""))];
            string stem = BpmnFileNames.ForFamily(byId[cluster.Medoid].Identity.Name, cluster.ClusterId, outcome.Members.Count);
            fileNames[cluster.ClusterId] = stem;
            await folder.WriteJsonAsync($"consolidated/{stem}.json", combined, token);
            XDocument xml = BpmnSerializer.ToXml(
                BpmnBuilder.Build(cluster.ClusterId, combined.Identity.Name, combined, sources, names, allNames), state.ToolVersion);
            await folder.WriteBytesAsync($"consolidated/{stem}.bpmn", BpmnSerializer.ToBytes(xml), token);
            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);
            if (errors.Count > 0)
            {
                state.Fail(ExitCode.RunFailed, $"consolidated/{stem}.bpmn fails BPMN 2.0 schema validation: {string.Join(" | ", errors.Take(3))}");
            }
        }

        state.CombinedFiles = fileNames;
        await folder.WriteTextAsync("reports/consolidation.md", Markdown(outcomes, byId, fileNames, state.BpmnFiles), token);
        state.Counts["consolidation.combined"] = outcomes.Count(outcome => outcome.Combined is not null);
        state.Counts["consolidation.skipped"] = outcomes.Count(outcome => outcome.Combined is null);
        state.Counts["consolidation.workflowsCombined"] = outcomes.Where(outcome => outcome.Combined is not null).Sum(outcome => outcome.Members.Count);
        state.StagesRun.Add("consolidation");
        logger.LogInformation("Consolidation: {Combined} families combined, {Skipped} skipped", state.Counts["consolidation.combined"], state.Counts["consolidation.skipped"]);
    }

    private static string Markdown(IReadOnlyList<CombineOutcome> outcomes, IReadOnlyDictionary<Guid, WorkflowIr> documents,
        IReadOnlyDictionary<string, string> fileNames, IReadOnlyDictionary<Guid, string> bpmnFiles)
    {
        StringBuilder text = new();
        text.AppendLine("# Consolidation").AppendLine();
        text.AppendLine("Each cohesive family is combined into one workflow as a set union: steps every member shares appear once; where members "
            + "differ, a *Variant* gateway names exactly which members take which path; every value any member sets is kept with the members "
            + "that set it. **Check each combined BPMN against the members' own files in `bpmn/`** — this file is the index for that check.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Combined: **{outcomes.Count(outcome => outcome.Combined is not null)}** families. "
            + $"Not combined: **{outcomes.Count(outcome => outcome.Combined is null)}**.").AppendLine();

        foreach (CombineOutcome outcome in outcomes.Where(outcome => outcome.Combined is not null))
        {
            WorkflowIr combined = outcome.Combined!;
            int variants = CountVariants(combined.Steps);
            text.AppendLine(CultureInfo.InvariantCulture, $"## {combined.Identity.Name}").AppendLine();
            string reconciliation = outcome.ReconciliationErrors.Count == 0
                ? "every member step accounted for exactly once."
                : string.Create(CultureInfo.InvariantCulture, $"**{outcome.ReconciliationErrors.Count} reconciliation error(s)**.");
            text.AppendLine(CultureInfo.InvariantCulture, $"`consolidated/{fileNames.GetValueOrDefault(outcome.ClusterId, outcome.ClusterId)}.bpmn` — {outcome.Members.Count} members, {variants} variant split(s), {reconciliation}");
            text.AppendLine().AppendLine("| Member | Workflow id | Original BPMN | Trigger |").AppendLine("|---|---|---|---|");
            foreach (Guid member in outcome.Members)
            {
                WorkflowIr document = documents[member];
                WorkflowTrigger trigger = document.Trigger;
                string triggerText = string.Join(", ", new[]
                {
                    trigger.OnCreate ? "create" : null,
                    trigger.OnUpdateFields.Count > 0 ? "update(" + string.Join(",", trigger.OnUpdateFields) + ")" : null,
                    trigger.OnDelete ? "delete" : null,
                    trigger.OnDemand ? "on demand" : null
                }.OfType<string>());
                text.AppendLine(CultureInfo.InvariantCulture, $"| {document.Identity.Name} | `{member:D}` | `bpmn/{bpmnFiles.GetValueOrDefault(member, member.ToString("D"))}.bpmn` | {triggerText} |");
            }
            text.AppendLine();
        }

        text.AppendLine("## Not combined").AppendLine();
        foreach (CombineOutcome outcome in outcomes.Where(outcome => outcome.Combined is null))
        {
            string members = string.Join(", ", outcome.Members.Select(member => documents[member].Identity.Name));
            text.AppendLine(CultureInfo.InvariantCulture, $"- `{outcome.ClusterId}` ({members}): {outcome.SkippedBecause}");
        }
        return text.ToString();
    }

    private static int CountVariants(IReadOnlyList<StepNode> steps)
    {
        return steps.Sum(step => (step.Kind == StepKind.Variant ? 1 : 0) + step.Branches.Sum(branch => CountVariants(branch.Steps)));
    }
}
