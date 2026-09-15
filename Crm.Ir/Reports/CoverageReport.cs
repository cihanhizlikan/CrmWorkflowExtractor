using System.Globalization;
using System.Text;
using Crm.Ir.Parsing;

namespace Crm.Ir.Reports;

/// <summary>A parsed workflow's name and coverage, the input to <c>parse-coverage.md</c>.</summary>
public sealed record WorkflowCoverage(Guid WorkflowId, string Name, IReadOnlyList<CoverageObservation> Observations);

/// <summary>§4.4: every construct encountered, with counts, split into mapped / support / unmapped, and every workflow with a gap.</summary>
public static class CoverageReport
{
    public static string Markdown(IReadOnlyList<WorkflowCoverage> workflows, int parseFailures)
    {
        List<CoverageObservation> all = [.. workflows.SelectMany(workflow => workflow.Observations)];
        int unmappedTotal = all.Count(observation => observation.Status == CoverageStatus.Unmapped);
        int stepTotal = all.Count(observation => observation.Status != CoverageStatus.Support);

        StringBuilder text = new();
        text.AppendLine("# Parse coverage").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Workflows parsed: **{workflows.Count}**. XAML that failed to parse at all: **{parseFailures}**.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Elements seen: **{all.Count}** — step-level **{stepTotal}**, of which unmapped **{unmappedTotal}**.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Workflows with at least one unmapped construct: **{workflows.Count(HasGap)}**.").AppendLine();
        text.AppendLine("*Mapped* became an IR step. *Support* is machinery a step consumes (variables, arguments, helper activities). "
            + "*Unmapped* is not understood and is shown as an explicitly unmapped task in the BPMN. Fix the top of the unmapped list first.").AppendLine();

        text.AppendLine("## Constructs by frequency").AppendLine();
        text.AppendLine("| Construct | Status | Occurrences | Workflows |").AppendLine("|---|---|---:|---:|");
        foreach ((string Construct, CoverageStatus Status, int Count, int Workflows) group in workflows
            .SelectMany(workflow => workflow.Observations.Select(observation => (workflow.WorkflowId, observation)))
            .GroupBy(pair => (pair.observation.Construct, pair.observation.Status))
            .Select(group => (group.Key.Construct, group.Key.Status, Count: group.Count(), Workflows: group.Select(pair => pair.WorkflowId).Distinct().Count()))
            .OrderBy(row => row.Status == CoverageStatus.Unmapped ? 0 : row.Status == CoverageStatus.Mapped ? 1 : 2)
            .ThenByDescending(row => row.Count)
            .ThenBy(row => row.Construct, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| `{group.Construct}` | {group.Status} | {group.Count} | {group.Workflows} |");
        }

        text.AppendLine().AppendLine("## Workflows with unmapped constructs").AppendLine();
        foreach (WorkflowCoverage workflow in workflows.Where(HasGap).OrderBy(workflow => workflow.Name, StringComparer.Ordinal))
        {
            IEnumerable<string> gaps = workflow.Observations
                .Where(observation => observation.Status == CoverageStatus.Unmapped)
                .Select(observation => $"`{observation.Construct}` at {(observation.Path.Length == 0 ? "root" : observation.Path)}")
                .Distinct(StringComparer.Ordinal);
            text.AppendLine(CultureInfo.InvariantCulture, $"- **{workflow.Name}** (`{workflow.WorkflowId:D}`): {string.Join("; ", gaps)}");
        }
        return text.ToString();
    }

    private static bool HasGap(WorkflowCoverage workflow)
    {
        return workflow.Observations.Any(observation => observation.Status == CoverageStatus.Unmapped);
    }
}
