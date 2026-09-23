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
        Sheet sheet = new(SheetNames.Unmapped, "is_akisi", "is_akisi_id", "yapi", "adim_yolu");
        foreach (WorkflowCoverage workflow in coverage.OrderBy(workflow => workflow.Name, StringComparer.Ordinal))
        {
            foreach (CoverageObservation observation in workflow.Observations
                .Where(observation => observation.Status == CoverageStatus.Unmapped)
                .DistinctBy(observation => (observation.Construct, observation.Path)))
            {
                sheet.Row(workflow.Name, workflow.WorkflowId, observation.Construct, observation.Path.Length == 0 ? "kök" : observation.Path);
            }
        }
        return sheet;
    }

    public static Sheet Constructs(IReadOnlyList<WorkflowCoverage> coverage)
    {
        Sheet sheet = new(SheetNames.Constructs, "yapi", "durum", "gorulme", "is_akisi_sayisi");
        foreach ((string construct, CoverageStatus status, int count, int workflows) in coverage
            .SelectMany(workflow => workflow.Observations.Select(observation => (workflow.WorkflowId, observation)))
            .GroupBy(pair => (pair.observation.Construct, pair.observation.Status))
            .Select(group => (group.Key.Construct, group.Key.Status, Count: group.Count(), Workflows: group.Select(pair => pair.WorkflowId).Distinct().Count()))
            .OrderBy(row => row.Status == CoverageStatus.Unmapped ? 0 : row.Status == CoverageStatus.Mapped ? 1 : 2)
            .ThenByDescending(row => row.Count))
        {
            sheet.Row(construct, Status(status), count, workflows);
        }
        return sheet;
    }

    public static Sheet Drift(DriftReport report)
    {
        Sheet sheet = new(SheetNames.Drift, "is_akisi", "durum", "tanim_id", "etkinlestirme_id", "yapi_farkli");
        foreach (DriftFinding finding in report.Drifted)
        {
            sheet.Row(finding.Name, "tanım ile çalışan kopya farklı", finding.DefinitionId, finding.ActivationId, finding.StructureDiffers);
        }
        foreach (WorkflowInventoryRecord record in report.DraftDefinitions)
        {
            sheet.Row(record.Name, record.ActiveWorkflowId is null ? "taslak, etkinleştirmesi yok" : "taslak, etkinleştirmesi var",
                record.WorkflowId, record.ActiveWorkflowId, null);
        }
        foreach (WorkflowInventoryRecord record in report.DefinitionsWithoutActivation)
        {
            sheet.Row(record.Name, "hiç etkinleştirme kaydı yok", record.WorkflowId, null, null);
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
