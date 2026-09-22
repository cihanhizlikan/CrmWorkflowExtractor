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
        text.AppendLine("# Ayrıştırma kapsamı").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Ayrıştırılan iş akışı: **{workflows.Count}**. Hiç ayrıştırılamayan XAML: **{parseFailures}**.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Görülen öğe: **{all.Count}** — adım düzeyinde **{stepTotal}**, bunların **{unmappedTotal}** tanesi okunamadı.");
        text.AppendLine(CultureInfo.InvariantCulture, $"En az bir yapısı okunamayan iş akışı: **{workflows.Count(HasGap)}**.").AppendLine();
        text.AppendLine("*Eşlendi*: ara modelde bir adıma dönüştü. *Yardımcı*: bir adımın kullandığı altyapıdır (değişkenler, bağımsız değişkenler, yardımcı etkinlikler). "
            + "*Okunamadı*: anlaşılamamıştır ve BPMN içinde açıkça okunamadı olarak işaretlenmiş bir görev olarak görünür. Önce listenin başındakileri ele alın.").AppendLine();

        text.AppendLine("## Sıklığa göre yapılar").AppendLine();
        text.AppendLine("| Yapı | Durum | Görülme | İş akışı |").AppendLine("|---|---|---:|---:|");
        foreach ((string Construct, CoverageStatus Status, int Count, int Workflows) group in workflows
            .SelectMany(workflow => workflow.Observations.Select(observation => (workflow.WorkflowId, observation)))
            .GroupBy(pair => (pair.observation.Construct, pair.observation.Status))
            .Select(group => (group.Key.Construct, group.Key.Status, Count: group.Count(), Workflows: group.Select(pair => pair.WorkflowId).Distinct().Count()))
            .OrderBy(row => row.Status == CoverageStatus.Unmapped ? 0 : row.Status == CoverageStatus.Mapped ? 1 : 2)
            .ThenByDescending(row => row.Count)
            .ThenBy(row => row.Construct, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| `{group.Construct}` | {Status(group.Status)} | {group.Count} | {group.Workflows} |");
        }

        text.AppendLine().AppendLine("## Yapısı okunamayan iş akışları").AppendLine();
        foreach (WorkflowCoverage workflow in workflows.Where(HasGap).OrderBy(workflow => workflow.Name, StringComparer.Ordinal))
        {
            IEnumerable<string> gaps = workflow.Observations
                .Where(observation => observation.Status == CoverageStatus.Unmapped)
                .Select(observation => $"`{observation.Construct}` — {(observation.Path.Length == 0 ? "kök" : observation.Path)}")
                .Distinct(StringComparer.Ordinal);
            text.AppendLine(CultureInfo.InvariantCulture, $"- **{workflow.Name}** (`{workflow.WorkflowId:D}`): {string.Join("; ", gaps)}");
        }
        return text.ToString();
    }

    /// <summary>The three coverage verdicts as the report prints them.</summary>
    private static string Status(CoverageStatus status)
    {
        return status switch
        {
            CoverageStatus.Mapped => "Eşlendi",
            CoverageStatus.Support => "Yardımcı",
            _ => "Okunamadı"
        };
    }

    private static bool HasGap(WorkflowCoverage workflow)
    {
        return workflow.Observations.Any(observation => observation.Status == CoverageStatus.Unmapped);
    }
}
