using System.Globalization;
using System.Text;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;

namespace Crm.Cli.Reports;

/// <summary>Human-readable M1 output: the run summary printed to the console and <c>reports/inventory.md</c>.</summary>
public static class InventoryReport
{
    public static string Summary(RunState state)
    {
        StringBuilder text = new();
        Line(text, "Run", $"{state.RunId}   ({state.RunRoot})");
        Line(text, "Tool version", state.ToolVersion);
        Line(text, "Organization", state.OrganizationUrl ?? "(not reached)");
        Line(text, "Authenticated as", state.Identity is null
            ? "(unknown — WhoAmI did not complete)"
            : $"{state.Identity.DomainName ?? "?"} ({state.Identity.FullName ?? "?"}) {state.Identity.UserId:D}");

        if (state.Privileges.Count > 0)
        {
            text.AppendLine("Privileges (organization-level read required, §2.4):");
            foreach (PrivilegeFinding finding in state.Privileges)
            {
                text.Append(CultureInfo.InvariantCulture, $"    {finding.Privilege.Name,-34} {finding.Privilege.Table,-28} {finding.Depth,-8} {finding.Verdict}").AppendLine();
            }
        }

        if (state.Reconciliation is ReconciliationResult result)
        {
            InventoryCounts counts = result.Counts;
            Line(text, "Count chain", string.Create(CultureInfo.InvariantCulture,
                $"$count {counts.ApiCount} -> retrieved {counts.Retrieved} (definitions {counts.Definitions} · activations {counts.Activations} · templates {counts.Templates} · other {counts.OtherType})"));
            Line(text, "Not designer-authored", string.Create(CultureInfo.InvariantCulture,
                $"{counts.DefinitionsNotDesignerAuthored} definition(s) → manual-review/, not parsed"));
            Line(text, "Distinct owners", counts.DistinctOwners.ToString(CultureInfo.InvariantCulture));
        }

        if (state.CountChain.Count > 0)
        {
            text.AppendLine("Count chain (§8):");
            foreach (CountLink link in state.CountChain)
            {
                text.Append("    ").AppendLine(link.ToString());
            }
        }
        if (state.Counts.ContainsKey("bpmn.written"))
        {
            Line(text, "Output", string.Create(CultureInfo.InvariantCulture,
                $"{Get(state, "ir.documents")} IR · {Get(state, "bpmn.written")} BPMN · {Get(state, "clusters.families")} families of 2+ · "
                + $"{Get(state, "consolidation.combined")} combined · {Get(state, "ir.workflowsWithUnmapped")} with unmapped constructs"));
            Line(text, "Read first", Path.Combine(state.RunRoot, "reports", "report.md"));
        }

        Block(text, "WARNINGS", state.Warnings);
        Block(text, "FAILURES", state.Failures);
        Line(text, "Status", $"{state.Status}   exit code {(int)state.ExitCode} ({state.ExitCode})");
        return text.ToString();
    }

    public static string Markdown(RunState state, IReadOnlyList<WorkflowInventoryRecord> records)
    {
        StringBuilder text = new();
        text.AppendLine("# Inventory — " + state.RunId).AppendLine();
        text.AppendLine("```").Append(Summary(state)).AppendLine("```").AppendLine();

        text.AppendLine("## Records by category, type and state").AppendLine();
        text.AppendLine("| Category | Type | State | Count |").AppendLine("|---|---|---|---:|");
        foreach (IGrouping<(string Category, string Type, string State), WorkflowInventoryRecord> group in records
            .GroupBy(record => (Category: record.Category.Label, Type: record.Type.Label, State: record.State.Label))
            .OrderBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Type, StringComparer.Ordinal)
            .ThenBy(group => group.Key.State, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Key.Category} | {group.Key.Type} | {group.Key.State} | {group.Count()} |").AppendLine();
        }
        text.AppendLine();

        text.AppendLine("## Option-set values: handout table vs server label").AppendLine();
        text.AppendLine("Every distinct value observed, with the label from the §3.1 table and the label the server itself returned. "
            + "A server label in the user's language differs in wording; a different *meaning* is a disagreement to record in the plan.").AppendLine();
        text.AppendLine("| Column | Raw | Handout label | Server label | Records |").AppendLine("|---|---:|---|---|---:|");
        foreach (IGrouping<(string Column, int Raw, string Tool, string Server), ObservedOption> group in records
            .SelectMany(record => record.ObservedOptions)
            .GroupBy(option => (Column: option.Column, Raw: option.Raw, Tool: option.ToolLabel, Server: option.ServerLabel ?? "(none)"))
            .OrderBy(group => group.Key.Column, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Raw)
            .ThenBy(group => group.Key.Server, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Key.Column} | {group.Key.Raw} | {group.Key.Tool} | {group.Key.Server} | {group.Count()} |").AppendLine();
        }
        return text.ToString();
    }

    private static void Line(StringBuilder text, string label, string value)
    {
        text.Append(CultureInfo.InvariantCulture, $"{label + ":",-24}{value}").AppendLine();
    }

    private static void Block(StringBuilder text, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }
        const int shown = 25;
        text.Append(CultureInfo.InvariantCulture, $"{title} ({lines.Count}):").AppendLine();
        foreach (string line in lines.Take(shown))
        {
            text.Append("  ! ").AppendLine(line);
        }
        if (lines.Count > shown)
        {
            text.Append(CultureInfo.InvariantCulture, $"  … {lines.Count - shown} more in logs/warnings.txt and reports/report.md").AppendLine();
        }
    }

    private static int Get(RunState state, string key)
    {
        return state.Counts.TryGetValue(key, out int value) ? value : 0;
    }
}
