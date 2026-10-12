using Crm.Cli.Stages;
using Crm.Ir.Model;
using Crm.Ir.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// The process-mining sheets: how often each stage was entered, and each step taken with how long it took. For
/// Enterprise Architecture only — <c>surec-madenciligi.xlsx</c> is never part of the delivered package.
/// </summary>
public static class ProcessMiningReport
{
    /// <summary>A step the stage record does not lead to on any outcome: it happened, but not the way the map is drawn.</summary>
    public const string NotInDesign = "tasarımda yok";

    public static Sheet Entries(ProcessMining mining, IReadOnlyList<CaseStage> stages)
    {
        Dictionary<Guid, CaseStage> byId = stages.ToDictionary(stage => stage.Id);
        Sheet sheet = new(SheetNames.StageEntries, "talep_konusu", "asama", "giris_sayisi", "son_kaydi_bu_asamada_olan_talep", "asama_id");
        foreach (MinedStage stage in mining.Stages
            .OrderBy(stage => TurkishFold.Fold(Subject(byId, stage.Stage)), StringComparer.Ordinal)
            .ThenByDescending(stage => stage.Entries))
        {
            sheet.Row(Subject(byId, stage.Stage), Name(byId, stage.Stage), stage.Entries, stage.LastRecorded, stage.Stage);
        }
        return sheet;
    }

    public static Sheet Steps(ProcessMining mining, IReadOnlyList<CaseStage> stages)
    {
        Dictionary<Guid, CaseStage> byId = stages.ToDictionary(stage => stage.Id);
        Sheet sheet = new(SheetNames.Steps, "talep_konusu", "kaynak_asama", "hedef_asama", "sonuc", "gecis_sayisi",
            "ortalama_saat", "medyan_saat", "yuzde90_saat", "ortalama_is_gunu", "medyan_is_gunu", "yuzde90_is_gunu",
            "kaynak_asama_id", "hedef_asama_id");
        foreach (MinedStep step in mining.Steps
            .OrderBy(step => TurkishFold.Fold(Subject(byId, step.From)), StringComparer.Ordinal)
            .ThenByDescending(step => step.Count))
        {
            sheet.Row(Subject(byId, step.From), Name(byId, step.From), Name(byId, step.To), Outcomes(byId, step), step.Count,
                step.MeanHours, step.MedianHours, step.P90Hours, step.MeanWorkDays, step.MedianWorkDays, step.P90WorkDays, step.From, step.To);
        }
        return sheet;
    }

    /// <summary>
    /// Which of the source stage's outcomes lead to the target, read off the stage record: the history says where a
    /// case went, the record says on which outcome a case goes there.
    /// </summary>
    private static string Outcomes(Dictionary<Guid, CaseStage> byId, MinedStep step)
    {
        if (!byId.TryGetValue(step.From, out CaseStage? from))
        {
            return NotInDesign;
        }
        List<string> outcomes = [.. from.Exits.Where(exit => exit.NextStage == step.To).Select(exit => exit.Outcome)];
        return outcomes.Count == 0 ? NotInDesign : string.Join(" / ", outcomes);
    }

    private static string Subject(Dictionary<Guid, CaseStage> byId, Guid stage)
    {
        return byId.TryGetValue(stage, out CaseStage? found) ? found.Process : "";
    }

    private static string Name(Dictionary<Guid, CaseStage> byId, Guid stage)
    {
        return byId.TryGetValue(stage, out CaseStage? found) ? found.ShortName : stage.ToString("D") + " (aşama kaydı yok)";
    }
}
