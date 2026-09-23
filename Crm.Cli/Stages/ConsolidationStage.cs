using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Cli.Reports;
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
            await folder.WriteJsonAsync($"{RunPaths.Combined}/{stem}.json", combined, token);
            XDocument xml = BpmnSerializer.ToXml(
                BpmnBuilder.Build(cluster.ClusterId, combined.Identity.Name, combined, sources, names, allNames), state.ToolVersion);
            await folder.WriteBytesAsync($"{RunPaths.Combined}/{stem}.bpmn", BpmnSerializer.ToBytes(xml), token);
            IReadOnlyList<string> errors = BpmnSchemaValidator.Validate(xml);
            if (errors.Count > 0)
            {
                state.Fail(ExitCode.RunFailed, $"{RunPaths.Combined}/{stem}.bpmn BPMN 2.0 şemasına uymuyor: {string.Join(" | ", errors.Take(3))}");
            }
        }

        state.CombinedFiles = fileNames;
        state.Sheets[SheetNames.Consolidation] = BuildSheet(outcomes, byId, fileNames, state.BpmnFiles);
        state.Counts["consolidation.combined"] = outcomes.Count(outcome => outcome.Combined is not null);
        state.Counts["consolidation.skipped"] = outcomes.Count(outcome => outcome.Combined is null);
        state.Counts["consolidation.workflowsCombined"] = outcomes.Where(outcome => outcome.Combined is not null).Sum(outcome => outcome.Members.Count);
        state.StagesRun.Add(RunStages.Consolidation);
        logger.LogInformation("Consolidation: {Combined} families combined, {Skipped} skipped", state.Counts["consolidation.combined"], state.Counts["consolidation.skipped"]);
    }

    /// <summary>
    /// The index for checking the combining: one row per member, with the combined model it went into and its own
    /// diagram beside it, so a reader can open both and see whether they belong together.
    /// </summary>
    private static Sheet BuildSheet(IReadOnlyList<CombineOutcome> outcomes, IReadOnlyDictionary<Guid, WorkflowIr> documents,
        IReadOnlyDictionary<string, string> fileNames, IReadOnlyDictionary<Guid, string> bpmnFiles)
    {
        Sheet sheet = new(SheetNames.Consolidation, "aile", "durum", "birlesik_dosya", "uye", "uye_bpmn", "tetikleyici", "cesitleme_sayisi", "mutabakat");
        foreach (CombineOutcome outcome in outcomes)
        {
            string combinedFile = fileNames.TryGetValue(outcome.ClusterId, out string? stem) ? $"{RunPaths.Combined}/{stem}.bpmn" : "";
            int variants = outcome.Combined is WorkflowIr combined ? CountVariants(combined.Steps) : 0;
            string state = outcome.Combined is null ? "birleştirilmedi: " + outcome.SkippedBecause : "birleştirildi";
            string reconciliation = outcome.ReconciliationErrors.Count == 0
                ? "her üye adımının hesabı verildi"
                : string.Create(CultureInfo.InvariantCulture, $"{outcome.ReconciliationErrors.Count} mutabakat hatası");
            foreach (Guid member in outcome.Members)
            {
                WorkflowIr document = documents[member];
                sheet.Row(outcome.ClusterId, state, combinedFile, document.Identity.Name,
                    $"{RunPaths.Bpmn}/{bpmnFiles.GetValueOrDefault(member, member.ToString("D"))}.bpmn",
                    TriggerText(document.Trigger), variants, reconciliation);
            }
        }
        return sheet;
    }

    private static string TriggerText(WorkflowTrigger trigger)
    {
        return string.Join(", ", new[]
        {
            trigger.OnCreate ? "oluşturma" : null,
            trigger.OnUpdateFields.Count > 0 ? "güncelleme(" + string.Join(",", trigger.OnUpdateFields) + ")" : null,
            trigger.OnDelete ? "silme" : null,
            trigger.OnDemand ? "istek üzerine" : null
        }.OfType<string>());
    }

    private static int CountVariants(IReadOnlyList<StepNode> steps)
    {
        return steps.Sum(step => (step.Kind == StepKind.Variant ? 1 : 0) + step.Branches.Sum(branch => CountVariants(branch.Steps)));
    }
}
