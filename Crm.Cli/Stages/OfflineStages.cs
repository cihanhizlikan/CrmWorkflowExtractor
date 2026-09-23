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

        await WriteAnalysisAsync(folder, state, documents, families, usage, token);
    }

    /// <summary>
    /// Last, because it gathers what every stage before it learned. One workbook per question the reader has: what
    /// the work is, which of these are the same, what touches what, and what reaches outside CRM.
    /// </summary>
    private static async Task WriteAnalysisAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents,
        SimilarityResult families, UsageEvidence? usage, CancellationToken token)
    {
        CallGraph calls = CallGraph.Build(documents);
        state.Sheets[SheetNames.Plan] = MigrationPlan.Build(state, documents, families, usage);
        state.Sheets[SheetNames.CallGraph] = calls.Build();
        state.Sheets[SheetNames.Trees] = calls.BuildTrees();
        state.Sheets[SheetNames.DataFootprint] = DataFootprint.Build(documents);
        state.Sheets[SheetNames.Cascades] = DataFootprint.BuildCascades(documents);
        state.Sheets[SheetNames.ExternalDependencies] = ExternalSystems.BuildDependencies(documents);
        state.Sheets[SheetNames.Addresses] = ExternalSystems.BuildAddresses(documents);

        state.Counts["external.activities"] = ExternalSystems.Dependencies(documents).Count;
        state.Counts["external.addresses"] = ExternalSystems.Addresses(documents).Select(address => address.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        state.Counts["callGraph.entryPoints"] = documents.Count(document => calls.RoleOf(document.Identity.WorkflowId) == CallGraph.EntryPoint);
        state.Counts["callGraph.buildingBlocks"] = calls.CalledBy.Count;
        state.Counts["data.fields"] = DataFootprint.Fields(documents).Count;
        state.Counts["data.sharedFields"] = DataFootprint.Fields(documents).Count(use => use.Writers.Count > 1);
        IReadOnlyList<Cascade> allCascades = DataFootprint.Cascades(documents);
        state.Counts["data.cascades"] = allCascades.Count;
        state.Counts["data.cascadePairs"] = allCascades.Select(cascade => (cascade.Source, cascade.Target)).Distinct().Count();

        // Each workbook opens with its own guide, so a reader who has the file has everything the file needs.
        state.Sheets[SheetNames.Guide] = Guides.Plan(state, documents.Count,
            documents.Count(document => MigrationPlan.IsLiveProcess(document)), Count(state, "usage.drafts"),
            Count(state, "clusters.suppliedHeldApart"), Count(state, "callGraph.buildingBlocks"));
        await WriteWorkbookAsync(folder, state, RunPaths.PlanWorkbook,
            [SheetNames.Guide, SheetNames.Plan, SheetNames.Usage, SheetNames.CallGraph, SheetNames.Trees,
             SheetNames.Diagrams, SheetNames.Unmapped, SheetNames.Constructs, SheetNames.Drift], token);

        state.Sheets[SheetNames.Guide] = Guides.Families(Count(state, "clusters.families"), Count(state, "consolidation.combined"),
            Count(state, "consolidation.skipped"), Count(state, "usage.drafts"), Count(state, "clusters.suppliedHeldApart"));
        await WriteWorkbookAsync(folder, state, RunPaths.FamilyWorkbook,
            [SheetNames.Guide, SheetNames.Families, SheetNames.Consolidation, SheetNames.Pairs, SheetNames.Drafts, SheetNames.Supplied], token);

        state.Sheets[SheetNames.Guide] = Guides.Data(Count(state, "data.fields"), Count(state, "data.sharedFields"),
            Count(state, "data.cascades"), Count(state, "data.cascadePairs"));
        await WriteWorkbookAsync(folder, state, RunPaths.DataWorkbook, [SheetNames.Guide, SheetNames.DataFootprint, SheetNames.Cascades], token);

        state.Sheets[SheetNames.Guide] = Guides.External(Count(state, "external.activities"), Count(state, "external.addresses"));
        await WriteWorkbookAsync(folder, state, RunPaths.ExternalSystemsWorkbook,
            [SheetNames.Guide, SheetNames.ExternalDependencies, SheetNames.Addresses], token);
        state.StagesRun.Add(RunStages.MigrationPlan);
    }

    private static int Count(RunState state, string key)
    {
        return state.Counts.TryGetValue(key, out int value) ? value : 0;
    }

    /// <summary>A sheet a stage never produced is left out rather than written empty: an empty tab reads like a bug.</summary>
    private static async Task WriteWorkbookAsync(RunFolder folder, RunState state, string path, IReadOnlyList<string> sheetNames, CancellationToken token)
    {
        List<Sheet> sheets = [.. sheetNames.Select(name => state.Sheets.GetValueOrDefault(name)).OfType<Sheet>()];
        if (sheets.Count > 0)
        {
            await folder.WriteBytesAsync(path, ExcelWorkbook.Build(sheets), token);
        }
    }
}
