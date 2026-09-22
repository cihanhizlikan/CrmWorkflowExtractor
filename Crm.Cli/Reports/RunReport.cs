using System.Globalization;
using System.Text;
using Crm.Extract.Inventory;
using Crm.Similarity;

namespace Crm.Cli.Reports;

/// <summary>§7.4 <c>reports/report.md</c>: the one page to read first after a run.</summary>
public static class RunReport
{
    public static string Markdown(RunState state)
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Run report — {state.RunId}").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Status **{state.Status}** (exit code {(int)state.ExitCode}). Tool {state.ToolVersion}. Organization {state.OrganizationUrl ?? "(not reached)"}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Stages run: {string.Join(" → ", state.StagesRun)}.").AppendLine();

        text.AppendLine("## Count chain").AppendLine();
        text.AppendLine("Every workflow is accounted for from the API count to the BPMN files. A GAP line is an unexplained loss and fails the run.").AppendLine();
        foreach (CountLink link in state.CountChain)
        {
            text.Append("- ").AppendLine(link.ToString());
        }
        text.AppendLine();

        AppendInventory(text, state);
        AppendFamilies(text, state);

        text.AppendLine("## Other reports").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- `parse-coverage.md` — {Count(state, "ir.workflowsWithUnmapped")} workflow(s) with at least one unmapped construct; {Count(state, "ir.parseFailed")} XAML file(s) that did not parse.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `drift.md` — {Count(state, "drift.structureDiffers")} definition(s) whose running activation has different logic; {Count(state, "drift.draftDefinitions")} draft definition(s).");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `consolidation.md` — {Count(state, "consolidation.combined")} family(ies) combined covering {Count(state, "consolidation.workflowsCombined")} workflows; {Count(state, "consolidation.skipped")} not combined.");
        text.AppendLine("- `migration.csv` / `migration.md` — the worksheet: one row per workflow, live processes first.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `data-footprint.md` / `.csv` / `data-cascades.csv` — {Count(state, "data.sharedFields")} field(s) written by more than one workflow; {Count(state, "data.cascades")} write(s) that start another workflow.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `call-graph.md` / `call-graph.csv` — {Count(state, "callGraph.entryPoints")} entry point(s); {Count(state, "callGraph.buildingBlocks")} workflow(s) called by another.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `usage.md` / `usage.csv` — {Count(state, "usage.drafts")} draft definition(s) held apart from grouping; {Count(state, "usage.testNamedActive")} activated workflow(s) whose name reads like a test; {(state.StagesRun.Contains("usage") ? $"{Count(state, "usage.withLoggedRun")} of {Count(state, "usage.definitions")} definitions with a logged run" : "no usage evidence (Run:UsageFile)")}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `sensitive-literals.md` — {Count(state, "sensitive.findings")} finding(s). RESTRICTED: for the security team.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- `../manual-review/index.md` — {Count(state, "manualReview")} hand-authored workflow(s), not parsed.");
        text.AppendLine();

        text.AppendLine(CultureInfo.InvariantCulture, $"## Warnings ({state.Warnings.Count})").AppendLine();
        foreach (string warning in state.Warnings.Take(50))
        {
            text.Append("- ").AppendLine(warning);
        }
        if (state.Warnings.Count > 50)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- … and {state.Warnings.Count - 50} more in `logs/warnings.txt`.");
        }
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Failures ({state.Failures.Count})").AppendLine();
        foreach (string failure in state.Failures)
        {
            text.Append("- ").AppendLine(failure);
        }
        return text.ToString();
    }

    private static void AppendInventory(StringBuilder text, RunState state)
    {
        if (state.Records.Count == 0)
        {
            return;
        }
        text.AppendLine("## Workflows by category and state").AppendLine();
        List<string> states = [.. state.Records.Select(record => record.State.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        text.AppendLine("| Category | " + string.Join(" | ", states) + " | Total |");
        text.AppendLine("|---|" + string.Concat(states.Select(_ => "---:|")) + "---:|");
        foreach (IGrouping<string, WorkflowInventoryRecord> category in state.Records
            .Where(record => record.Type.Raw == WorkflowOptionSets.TypeDefinition)
            .GroupBy(record => record.Category.Label, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            IEnumerable<string> cells = states.Select(label => category.Count(record => record.State.Label == label).ToString(CultureInfo.InvariantCulture));
            text.AppendLine(CultureInfo.InvariantCulture, $"| {category.Key} | {string.Join(" | ", cells)} | {category.Count()} |");
        }
        text.AppendLine().AppendLine("Definitions only; activation records are copies of their definition and are counted in the chain above.").AppendLine();
    }

    private static void AppendFamilies(StringBuilder text, RunState state)
    {
        if (state.Similarity is not SimilarityResult similarity)
        {
            return;
        }
        text.AppendLine("## Families").AppendLine();
        text.AppendLine("| Family size | Families |").AppendLine("|---:|---:|");
        foreach (IGrouping<int, WorkflowCluster> size in similarity.Clusters.GroupBy(cluster => cluster.Members.Count).OrderByDescending(group => group.Key))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {size.Key} | {size.Count()} |");
        }
        text.AppendLine().AppendLine("### Largest 20 families").AppendLine();
        text.AppendLine("| Family | Size | Suggested starting point (medoid) | Weakest internal score | Chain depth | Cohesion |").AppendLine("|---|---:|---|---:|---:|---|");
        foreach (WorkflowCluster cluster in similarity.Clusters.Where(cluster => cluster.Members.Count > 1).Take(20))
        {
            string medoid = cluster.Members.First(member => member.WorkflowId == cluster.Medoid).Name;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{cluster.ClusterId}` | {cluster.Members.Count} | {medoid} | {cluster.MinimumInternalScore:0.00} | {cluster.ChainDepth} | {(cluster.LowCohesion ? "**low — split first**" : "ok")} |");
        }
        text.AppendLine();
    }

    private static int Count(RunState state, string key)
    {
        return state.Counts.TryGetValue(key, out int value) ? value : 0;
    }
}
