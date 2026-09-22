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

        // A Draft definition cannot start a run, so it is not grouped or combined with running logic. It keeps its IR
        // and BPMN and is listed on its own. The split is by state, never by name.
        List<WorkflowIr> runnable = [.. documents.Where(document => document.Identity.State != UsageStage.DraftState)];
        List<WorkflowIr> drafts = [.. documents.Where(document => document.Identity.State == UsageStage.DraftState)];
        SimilarityOptions similarity = settings.Similarity?.Value ?? new SimilarityOptions();
        SimilarityResult families = await SimilarityStage.RunAsync(folder, state, runnable, drafts, usage, similarity, logger, token);
        await ConsolidationStage.RunAsync(folder, state, runnable, families, logger, token);

        // Last, because it gathers what every stage before it learned into the one sheet the analysts work from.
        CallGraph calls = CallGraph.Build(documents);
        await folder.WriteTextAsync("reports/call-graph.md", calls.Markdown(), token);
        await folder.WriteBytesAsync("reports/call-graph.csv", calls.Csv(), token);
        await folder.WriteBytesAsync("reports/migration.csv", MigrationPlan.Csv(state, documents, families, usage), token);
        await folder.WriteTextAsync("reports/migration.md", MigrationPlan.Markdown(state, documents, families), token);
        state.Counts["callGraph.entryPoints"] = documents.Count(document => calls.RoleOf(document.Identity.WorkflowId) == CallGraph.EntryPoint);
        state.Counts["callGraph.buildingBlocks"] = calls.CalledBy.Count;
        state.StagesRun.Add("migration");
    }
}
