using Crm.Ir.Model;
using Crm.Ir.Stages;
using Crm.Ir.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// The first sheet of the plan: one row per ACTIVE stage of the machine a case moves through.
///
/// <para>
/// A row says where the stage leads on each outcome, which workflow it fires, what it texts the customer, and every
/// rule it carries — SLA, assignment, documents, closing. The workflows are steps in this machine, so the analysts
/// read it first and the workflow sheet second (chief analyst, 2026-10-09). Rows run in the order a case meets the
/// stages: by subcategory, then from each primary stage along its outcomes.
/// </para>
/// </summary>
public static class StageSheet
{
    public static Sheet Build(IReadOnlyList<CaseStage> stages, IReadOnlyDictionary<Guid, IReadOnlyList<string>> maps)
    {
        List<string> columns = ["surec", "asama", "kisa_ad", "birincil", "otomatik",
            "olumlu_sonraki_asama", "olumlu_is_akisi", "olumlu_sms",
            "olumsuz_sonraki_asama", "olumsuz_is_akisi", "olumsuz_sms",
            "iptal_sonraki_asama", "iptal_is_akisi", "iptal_sms",
            "kullaniciya_atanir"];
        columns.AddRange(CaseStageRules.All.Select(rule => rule.Column));
        columns.AddRange(["asama_akisi_bpmn", "asama_id"]);
        Sheet sheet = new(SheetNames.Stages, [.. columns]);

        Dictionary<Guid, CaseStage> byId = stages.ToDictionary(stage => stage.Id);
        foreach (CaseStage stage in Ordered(stages, byId))
        {
            List<object?> row = [stage.Process, stage.Name, stage.ShortName, YesNo(stage.Primary), YesNo(stage.Automatic)];
            foreach (string outcome in new[] { StageOutcome.Success, StageOutcome.Failure, StageOutcome.Cancel })
            {
                StageExit? exit = stage.Exits.FirstOrDefault(candidate => candidate.Outcome == outcome);
                row.Add(Next(exit, byId));
                row.Add(exit?.WorkflowName);
                row.Add(exit?.Sms);
            }
            row.Add(YesNo(stage.Fields.ContainsKey(CaseStageRules.AssignedUser)));
            row.AddRange(CaseStageRules.All.Select(rule => stage.Fields.GetValueOrDefault(rule.Field)));
            row.Add(maps.TryGetValue(stage.Id, out IReadOnlyList<string>? files) ? string.Join("; ", files) : null);
            row.Add(stage.Id);
            sheet.Row([.. row]);
        }
        return sheet;
    }

    /// <summary>
    /// The stage an outcome leads to, saying so when that stage is switched off or no longer exists — both are a
    /// dead end the new product must not quietly reproduce.
    /// </summary>
    private static string? Next(StageExit? exit, Dictionary<Guid, CaseStage> byId)
    {
        if (exit?.NextStage is not Guid id)
        {
            return null;
        }
        string name = exit.NextStageName ?? id.ToString("D");
        if (!byId.TryGetValue(id, out CaseStage? next))
        {
            return name + " (aşama kaydı yok)";
        }
        return next.Active ? name : name + " (pasif)";
    }

    /// <summary>Active stages only, by subcategory, each subcategory from its primary stages along their outcomes.</summary>
    private static List<CaseStage> Ordered(IReadOnlyList<CaseStage> stages, Dictionary<Guid, CaseStage> byId)
    {
        List<CaseStage> ordered = [];
        HashSet<Guid> placed = [];
        foreach (IGrouping<string, CaseStage> process in stages.Where(stage => stage.Active)
            .GroupBy(stage => stage.Process, StringComparer.Ordinal)
            .OrderBy(group => TurkishFold.Fold(group.Key), StringComparer.Ordinal))
        {
            Queue<CaseStage> queue = new(process.Where(stage => stage.Primary).OrderBy(stage => TurkishFold.Fold(stage.Name), StringComparer.Ordinal));
            while (queue.TryDequeue(out CaseStage? stage))
            {
                if (!placed.Add(stage.Id))
                {
                    continue;
                }
                ordered.Add(stage);
                foreach (StageExit exit in stage.Exits)
                {
                    if (exit.NextStage is Guid next && byId.TryGetValue(next, out CaseStage? following) && following.Active
                        && following.Process == stage.Process && !placed.Contains(next))
                    {
                        queue.Enqueue(following);
                    }
                }
            }
            ordered.AddRange(process.Where(stage => placed.Add(stage.Id)).OrderBy(stage => TurkishFold.Fold(stage.Name), StringComparer.Ordinal));
        }
        return ordered;
    }

    private static string YesNo(bool value)
    {
        return value ? "evet" : "hayır";
    }
}
