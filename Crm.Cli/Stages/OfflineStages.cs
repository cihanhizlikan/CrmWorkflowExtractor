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
        UsageStage.Summarize(state, documents, usage);

        // What is not this company's to rebuild leaves the plan and every stage after it: a Draft, which cannot start
        // a run; one CRM reports as part of a managed solution, which was shipped with the product; and one whose name
        // reads like a test AND has no logged run. All three keep their IR and BPMN and are listed in their own book.
        List<WorkflowIr> inScope = [.. MigrationPlan.InScope(documents, usage)];
        state.Counts["clusters.suppliedHeldApart"] = documents.Count(document => document.Identity.IsManaged == true);
        state.Counts["clusters.draftsHeldApart"] = documents.Count(document => document.Identity.IsManaged != true && document.Identity.State == UsageStage.DraftState);
        state.Counts["plan.excluded"] = documents.Count - inScope.Count;
        state.Counts["clusters.testNamedHeldApart"] = state.Counts["plan.excluded"]
            - state.Counts["clusters.draftsHeldApart"] - state.Counts["clusters.suppliedHeldApart"];
        SimilarityOptions similarity = settings.Similarity?.Value ?? new SimilarityOptions();
        SimilarityResult families = await SimilarityStage.RunAsync(folder, state, inScope, usage, similarity, logger, token);
        await BpmnStage.RunAsync(folder, state, documents, families, usage, logger, token);
        await ConsolidationStage.RunAsync(folder, state, inScope, families, logger, token);

        await WriteAnalysisAsync(folder, state, documents, families, usage, settings.Run.Value.LogoFile, token);
    }

    /// <summary>
    /// Last, because it gathers what every stage before it learned. One workbook per question the reader has: what
    /// the work is, which of these are the same, what touches what, and what reaches outside CRM.
    /// </summary>
    private static async Task WriteAnalysisAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents,
        SimilarityResult families, UsageEvidence? usage, string logoFile, CancellationToken token)
    {
        CallGraph calls = CallGraph.Build(documents);
        HashSet<Guid> inPlan = [.. MigrationPlan.InScope(documents, usage).Select(document => document.Identity.WorkflowId)];
        state.Sheets[SheetNames.Plan] = MigrationPlan.Build(state, documents, families, usage);
        state.Sheets[SheetNames.Excluded] = MigrationPlan.BuildExcluded(state, documents, usage);
        state.Sheets[SheetNames.CallGraph] = calls.Build();
        state.Sheets[SheetNames.Trees] = calls.BuildTrees();
        state.Sheets[SheetNames.DataFootprint] = DataFootprint.Build(documents);
        state.Sheets[SheetNames.Cascades] = DataFootprint.BuildCascades(documents);
        state.Sheets[SheetNames.ExternalDependencies] = ExternalSystems.BuildDependencies(documents);
        state.Sheets[SheetNames.Unmapped] = QualitySheets.Unmapped(state.Coverage, state.BpmnFiles);
        if (state.Drift is not null)
        {
            state.Sheets[SheetNames.Drift] = QualitySheets.Drift(state.Drift, inPlan);
        }
        state.Sheets[SheetNames.Addresses] = ExternalSystems.BuildAddresses(state.Addresses);

        state.Counts["external.activities"] = ExternalSystems.Dependencies(documents).Count;
        state.Counts["external.addresses"] = state.Addresses.Select(address => address.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        state.Counts["external.hosts"] = state.Addresses.Select(address => address.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        state.Counts["callGraph.entryPoints"] = documents.Count(document => calls.RoleOf(document.Identity.WorkflowId) == CallGraph.EntryPoint);
        state.Counts["callGraph.buildingBlocks"] = calls.CalledBy.Count;
        state.Counts["data.fields"] = DataFootprint.Fields(documents).Count;
        state.Counts["data.sharedFields"] = DataFootprint.Fields(documents).Count(use => use.Writers.Count > 1);
        IReadOnlyList<Cascade> allCascades = DataFootprint.Cascades(documents);
        state.Counts["data.cascades"] = allCascades.Count;
        state.Counts["data.cascadePairs"] = allCascades.Select(cascade => (cascade.Source, cascade.Target)).Distinct().Count();

        // Each workbook opens with its own guide, so a reader who has the file has everything the file needs.
        int inScopeCount = documents.Count - Count(state, "plan.excluded");
        state.Sheets[SheetNames.Guide] = Guides.Plan(inScopeCount,
            MigrationPlan.InScope(documents, usage).Count(MigrationPlan.IsLiveProcess),
            Count(state, "plan.excluded"), Count(state, "callGraph.buildingBlocks"));
        await WriteWorkbookAsync(folder, state, RunPaths.PlanWorkbook,
            [SheetNames.Guide, SheetNames.Plan, SheetNames.CallGraph, SheetNames.Trees, SheetNames.Drift, SheetNames.Unmapped], token);

        state.Sheets[SheetNames.Guide] = Guides.Excluded(Count(state, "plan.excluded"), Count(state, "usage.drafts"),
            Count(state, "clusters.suppliedHeldApart"), documents.Count);
        await WriteWorkbookAsync(folder, state, RunPaths.OutOfScopeWorkbook, [SheetNames.Guide, SheetNames.Excluded], token);

        state.Sheets[SheetNames.Guide] = Guides.Families(Count(state, "clusters.families"), Count(state, "consolidation.combined"),
            Count(state, "consolidation.skipped"));
        await WriteWorkbookAsync(folder, state, RunPaths.FamilyWorkbook,
            [SheetNames.Guide, SheetNames.Families, SheetNames.Consolidation, SheetNames.Pairs], token);

        state.Sheets[SheetNames.Guide] = Guides.Data(Count(state, "data.fields"), Count(state, "data.sharedFields"),
            Count(state, "data.cascades"), Count(state, "data.cascadePairs"));
        await WriteWorkbookAsync(folder, state, RunPaths.DataWorkbook, [SheetNames.Guide, SheetNames.DataFootprint, SheetNames.Cascades], token);

        state.Sheets[SheetNames.Guide] = Guides.External(Count(state, "external.activities"), Count(state, "external.addresses"), Count(state, "external.hosts"));
        await WriteWorkbookAsync(folder, state, RunPaths.ExternalSystemsWorkbook,
            [SheetNames.Guide, SheetNames.ExternalDependencies, SheetNames.Addresses], token);

        // Written last of all: it quotes the numbers and names a real workflow from everything above it.
        if (AnalystGuide.Build(state, documents, usage, logoFile) is byte[] guide)
        {
            await folder.WriteBytesAsync(RunPaths.AnalystGuide, guide, token);
        }
        else
        {
            state.Warnings.Add($"{RunPaths.AnalystGuide} yazılamadı: bu makinede Türkçe harfleri taşıyan ve gömülmesine izin veren bir yazı tipi bulunamadı.");
        }
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
