using Crm.Extract.Inventory;
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
    public static Sheet Unmapped(IReadOnlyList<WorkflowCoverage> coverage)
    {
        Sheet sheet = new(SheetNames.Unmapped, "is_akisi", "yapi", "adim_yolu", "is_akisi_id");
        foreach (WorkflowCoverage workflow in coverage.OrderBy(workflow => workflow.Name, StringComparer.Ordinal))
        {
            foreach (CoverageObservation observation in workflow.Observations
                .Where(observation => observation.Status == CoverageStatus.Unmapped)
                .DistinctBy(observation => (observation.Construct, observation.Path)))
            {
                sheet.Row(workflow.Name, observation.Construct, observation.Path.Length == 0 ? "kök" : observation.Path, workflow.WorkflowId);
            }
        }
        return sheet;
    }

    public static Sheet Drift(DriftReport report)
    {
        Sheet sheet = new(SheetNames.Drift, "is_akisi", "durum", "yapi_farkli", "tanim_id");
        foreach (DriftFinding finding in report.Drifted)
        {
            sheet.Row(finding.Name, "tanım ile çalışan kopya farklı", finding.StructureDiffers, finding.DefinitionId);
        }
        foreach (WorkflowInventoryRecord record in report.DraftDefinitions)
        {
            sheet.Row(record.Name, record.ActiveWorkflowId is null ? "taslak, etkinleştirmesi yok" : "taslak, etkinleştirmesi var",
                null, record.WorkflowId);
        }
        foreach (WorkflowInventoryRecord record in report.DefinitionsWithoutActivation)
        {
            sheet.Row(record.Name, "hiç etkinleştirme kaydı yok", null, record.WorkflowId);
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
