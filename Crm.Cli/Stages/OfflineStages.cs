using Crm.Cli.Configuration;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Similarity;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// The stages after retrieval. They read the run folder's evidence and nothing else — no network — so a failure
/// in one is diagnosable from the files, and the chain can be re-run over an existing run's raw/ folder.
/// </summary>
public static class OfflineStages
{
    public static async Task RunAsync(RunFolder folder, RunState state, ExtractorSettings settings, DateTimeOffset extractedAt, ILogger logger, CancellationToken token)
    {
        UsageEvidence? usage = await UsageStage.LoadAsync(folder, state, settings.Run.Value.UsageFile, logger, token);
        IReadOnlyList<WorkflowIr> documents = await IrStage.RunAsync(folder, state, extractedAt, logger, token);
        state.Documents = documents;
        await BpmnStage.RunAsync(folder, state, documents, logger, token);
        await UsageStage.WriteReportAsync(folder, state, documents, usage, token);

        // Two kinds of workflow are not this company's to rebuild, and neither is grouped or combined: a Draft, which
        // cannot start a run, and one CRM reports as part of a managed solution, which was shipped with the product.
        // Both keep their IR and BPMN and are listed on their own. The split is by what CRM says, never by name.
        List<WorkflowIr> supplied = [.. documents.Where(document => document.Identity.IsManaged == true)];
        List<WorkflowIr> drafts = [.. documents.Where(document => document.Identity.IsManaged != true && document.Identity.State == UsageStage.DraftState)];
        List<WorkflowIr> runnable = [.. documents.Where(document => document.Identity.IsManaged != true && document.Identity.State != UsageStage.DraftState)];
        SimilarityOptions similarity = settings.Similarity?.Value ?? new SimilarityOptions();
        SimilarityResult families = await SimilarityStage.RunAsync(folder, state, runnable, drafts, supplied, usage, similarity, logger, token);
        await ConsolidationStage.RunAsync(folder, state, runnable, families, logger, token);

        // Last, because it gathers what every stage before it learned into the one sheet the analysts work from.
        CallGraph calls = CallGraph.Build(documents);
        await folder.WriteTextAsync(RunPaths.CallGraph, calls.Markdown(), token);
        await folder.WriteBytesAsync(RunPaths.CallGraphCsv, calls.Csv(), token);
        await folder.WriteTextAsync(RunPaths.DataFootprint, DataFootprint.Markdown(documents), token);
        await folder.WriteBytesAsync(RunPaths.DataFootprintCsv, DataFootprint.Csv(documents), token);
        await folder.WriteBytesAsync(RunPaths.DataCascadesCsv, DataFootprint.CascadeCsv(documents), token);
        await folder.WriteBytesAsync(RunPaths.MigrationPlanCsv, MigrationPlan.Csv(state, documents, families, usage), token);
        await folder.WriteTextAsync(RunPaths.MigrationPlan, MigrationPlan.Markdown(state, documents, families), token);
        state.Counts["callGraph.entryPoints"] = documents.Count(document => calls.RoleOf(document.Identity.WorkflowId) == CallGraph.EntryPoint);
        state.Counts["callGraph.buildingBlocks"] = calls.CalledBy.Count;
        state.Counts["data.sharedFields"] = DataFootprint.Fields(documents).Count(use => use.Writers.Count > 1);
        IReadOnlyList<Cascade> allCascades = DataFootprint.Cascades(documents);
        state.Counts["data.cascades"] = allCascades.Count;
        state.Counts["data.cascadePairs"] = allCascades.Select(cascade => (cascade.Source, cascade.Target)).Distinct().Count();
        state.StagesRun.Add(RunStages.MigrationPlan);
    }
}
