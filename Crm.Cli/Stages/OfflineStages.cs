using Crm.Cli.Configuration;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
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
        state.Plugins = PluginStage.Load(folder, state);
        state.RunAuthority = RoleStage.Load(folder, state);
        IReadOnlyList<WorkflowIr> documents = await IrStage.RunAsync(folder, state, extractedAt, logger, token);
        state.Documents = documents;
        UsageStage.Summarize(state, documents, usage);

        // What is not this company's to rebuild leaves the plan: a Draft, which cannot start a run; one CRM reports
        // as part of a managed solution, which was shipped with the product; and one whose name reads like a test AND
        // has no logged run. All three keep their IR and BPMN and are listed in their own book.
        int inScope = MigrationPlan.InScope(documents, usage).Count();
        state.Counts["plan.supplied"] = documents.Count(document => document.Identity.IsManaged == true);
        state.Counts["plan.drafts"] = documents.Count(document => document.Identity.IsManaged != true && document.Identity.State == UsageStage.DraftState);
        state.Counts["plan.excluded"] = documents.Count - inScope;
        state.Counts["plan.testNamed"] = state.Counts["plan.excluded"] - state.Counts["plan.drafts"] - state.Counts["plan.supplied"];
        await BpmnStage.RunAsync(folder, state, documents, usage, logger, token);
        await StageMapStage.RunAsync(folder, state, documents, logger, token);

        await WriteAnalysisAsync(folder, state, documents, usage, settings.Run.Value.LogoFile, token);
    }

    /// <summary>
    /// Last, because it gathers what every stage before it learned. One workbook per question the reader has: what
    /// the work is, which of these are the same, what touches what, and what reaches outside CRM.
    /// </summary>
    private static async Task WriteAnalysisAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents,
        UsageEvidence? usage, string logoFile, CancellationToken token)
    {
        CallGraph calls = CallGraph.Build(documents);
        HashSet<Guid> inPlan = [.. MigrationPlan.InScope(documents, usage).Select(document => document.Identity.WorkflowId)];
        // The process first: the workflows are steps of it. An export made before the stages were collected
        // has none, and then the plan opens on the workflows as it always did.
        if (state.CaseStages.Any(stage => stage.Active))
        {
            state.Sheets[SheetNames.Stages] = StageSheet.Build(state.CaseStages, state.StageMapFiles);
        }
        state.Sheets[SheetNames.Plan] = MigrationPlan.Build(state, documents, usage);
        state.Sheets[SheetNames.Excluded] = MigrationPlan.BuildExcluded(state, documents, usage);
        state.Sheets[SheetNames.CallGraph] = calls.Build();
        state.Sheets[SheetNames.Trees] = calls.BuildTrees();
        state.Sheets[SheetNames.DataFootprint] = DataFootprint.Build(documents);
        state.Sheets[SheetNames.Cascades] = DataFootprint.BuildCascades(documents);
        ExternalSheets(state, documents);
        state.Sheets[SheetNames.Unmapped] = QualitySheets.Unmapped(state.Coverage, state.BpmnFiles);
        if (state.Drift is not null)
        {
            state.Sheets[SheetNames.Drift] = QualitySheets.Drift(state.Drift, inPlan);
        }
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
            Count(state, "plan.excluded"), Count(state, "callGraph.buildingBlocks"),
            Count(state, "caseStages.active"), Count(state, "stageMaps.written"));
        await WriteWorkbookAsync(folder, state, RunPaths.PlanWorkbook,
            [SheetNames.Guide, SheetNames.Stages, SheetNames.Plan, SheetNames.CallGraph, SheetNames.Trees, SheetNames.Drift, SheetNames.Unmapped], token);

        state.Sheets[SheetNames.Guide] = Guides.Excluded(Count(state, "plan.excluded"), Count(state, "usage.drafts"),
            Count(state, "plan.supplied"), documents.Count);
        await WriteWorkbookAsync(folder, state, RunPaths.OutOfScopeWorkbook, [SheetNames.Guide, SheetNames.Excluded], token);

        state.Sheets[SheetNames.Guide] = Guides.Data(Count(state, "data.fields"), Count(state, "data.sharedFields"),
            Count(state, "data.cascades"), Count(state, "data.cascadePairs"));
        await WriteWorkbookAsync(folder, state, RunPaths.DataWorkbook, [SheetNames.Guide, SheetNames.DataFootprint, SheetNames.Cascades], token);

        state.Sheets[SheetNames.Guide] = Guides.External(Count(state, "external.activities"), Count(state, "external.addresses"),
            Count(state, "external.hosts"), Count(state, "external.assemblyAddresses"), state.Plugins.Steps.Count);
        await WriteWorkbookAsync(folder, state, RunPaths.ExternalSystemsWorkbook,
            [SheetNames.Guide, SheetNames.ExternalDependencies, SheetNames.Addresses, SheetNames.Plugins], token);

        AuthoritySheets(state, MigrationPlan.InScope(documents, usage));
        await WriteWorkbookAsync(folder, state, RunPaths.RunAuthorityWorkbook,
            [SheetNames.Guide, SheetNames.RunRoles, SheetNames.RunAuthority], token);

        // Written last of all: it quotes the numbers and names a real workflow from everything above it.
        await folder.WriteBytesAsync(RunPaths.AnalystGuide, AnalystGuide.Build(state, documents, usage, logoFile), token);
        state.StagesRun.Add(RunStages.MigrationPlan);
    }

    /// <summary>
    /// Everything the run knows about what these workflows reach outside CRM. Gathered in one place because it is
    /// the one question whose answer comes from three different sources: the definitions, the registered code, and
    /// the assemblies behind it.
    /// </summary>
    private static void ExternalSheets(RunState state, IReadOnlyList<WorkflowIr> documents)
    {
        IReadOnlyList<ExternalDependency> dependencies = ExternalSystems.Dependencies(documents, state.Plugins);
        state.Sheets[SheetNames.ExternalDependencies] = ExternalSystems.BuildDependencies(documents, state.Plugins);
        state.Sheets[SheetNames.Addresses] = ExternalSystems.BuildAddresses(state.Addresses);
        if (state.Plugins.Steps.Count > 0)
        {
            state.Sheets[SheetNames.Plugins] = ExternalSystems.BuildPlugins(state.Plugins);
        }
        state.Counts["external.activities"] = dependencies.Count;
        state.Counts["external.assemblyAddresses"] = dependencies.SelectMany(dependency => dependency.Addresses)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        state.Counts["external.addresses"] = state.Addresses.Select(address => address.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        state.Counts["external.hosts"] = state.Addresses.Select(address => address.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    /// <summary>
    /// Who may start a process by hand. Two sheets rather than one joined table, because CRM grants the right
    /// estate-wide: a single "these people can run this workflow" column would read as a grant CRM never makes.
    /// </summary>
    private static void AuthoritySheets(RunState state, IEnumerable<WorkflowIr> inScope)
    {
        List<WorkflowIr> plan = [.. inScope];
        state.Sheets[SheetNames.RunRoles] = RunAuthorityReport.Roles(state.RunAuthority);
        state.Sheets[SheetNames.RunAuthority] = RunAuthorityReport.Authority(plan, state.RunAuthority, state.BpmnFiles);
        int onDemand = plan.Count(document => document.Identity.IsOnDemand == true);
        state.Counts["roles.onDemand"] = onDemand;
        state.Sheets[SheetNames.Guide] = Guides.RunAuthority(state.RunAuthority.Roles.Count,
            state.RunAuthority.Roles.Sum(role => role.Users), state.RunAuthority.Roles.Sum(role => role.Teams.Count),
            onDemand, plan.Count, state.RunAuthority.Note);
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
