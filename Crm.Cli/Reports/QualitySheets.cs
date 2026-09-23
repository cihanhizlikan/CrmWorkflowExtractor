using Crm.Extract.Xaml;
using Crm.Ir.Parsing;
using Crm.Ir.Reports;

namespace Crm.Cli.Reports;

/// <summary>
/// How much of the model can be trusted, as sheets: what the parser could not read, and where a workflow's running
/// copy differs from its definition. <c>Crm.Ir</c> and <c>Crm.Extract</c> cannot see <see cref="Sheet"/>
/// (structure.md), so their reports are turned into sheets here.
/// </summary>
public static class QualitySheets
{
    /// <summary>
    /// What the parser could not read, per workflow and construct. The locator is the diagram, not the XAML path:
    /// the path CRM leaves behind is an index that means nothing to a reader, while the diagram marks the very
    /// step OKUNAMADI. One row per construct per workflow — the same unreadable thing forty times is one problem.
    /// </summary>
    public static Sheet Unmapped(IReadOnlyList<WorkflowCoverage> coverage, IReadOnlyDictionary<Guid, string> bpmnFiles)
    {
        Sheet sheet = new(SheetNames.Unmapped, "is_akisi", "yapi", "kac_kez", "bpmn_dosyasi", "is_akisi_id");
        foreach (WorkflowCoverage workflow in coverage.OrderBy(workflow => workflow.Name, StringComparer.Ordinal))
        {
            foreach (IGrouping<string, CoverageObservation> construct in workflow.Observations
                .Where(observation => observation.Status == CoverageStatus.Unmapped)
                .GroupBy(observation => observation.Construct, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()))
            {
                sheet.Row(workflow.Name, construct.Key, construct.Count(),
                    bpmnFiles.TryGetValue(workflow.WorkflowId, out string? file) ? file + ".bpmn" : "", workflow.WorkflowId);
            }
        }
        return sheet;
    }

    /// <summary>
    /// The workflows whose running copy really differs from the definition the diagram was drawn from — every row
    /// is a thing to go and check in CRM. Pairs that differ only in formatting or in the per-record class names are
    /// left out, and so are the drafts: a draft is not in the plan, so a reader would look for it and not find it.
    /// </summary>
    public static Sheet Drift(DriftReport report, IReadOnlySet<Guid> inScope)
    {
        Sheet sheet = new(SheetNames.Drift, "is_akisi", "is_akisi_id");
        foreach (DriftFinding finding in report.Drifted.Where(finding => finding.StructureDiffers && inScope.Contains(finding.DefinitionId)))
        {
            sheet.Row(finding.Name, finding.DefinitionId);
        }
        return sheet;
    }

    private static string Status(CoverageStatus status)
    {
        return status switch
        {
            CoverageStatus.Mapped => "Eşlendi",
            CoverageStatus.Support => "Yardımcı",
            _ => "Okunamadı"
        };
    }
}
