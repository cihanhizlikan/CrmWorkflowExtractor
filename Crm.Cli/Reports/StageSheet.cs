using Crm.Ir.Model;
using Crm.Ir.Stages;
using Crm.Ir.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// The stage sheets of the plan: one row per request subject and its map, then one row per ACTIVE stage.
///
/// <para>
/// A stage row says where the stage leads on each outcome, which workflow it fires, what it texts the customer, and
/// every rule it carries — SLA, assignment, documents, closing. The workflows are steps in this process, so the
/// analysts read it first and the workflow sheet second (chief analyst, 2026-10-09). Rows run in the order a case
/// meets the stages: by subject, then from each starting stage along its outcomes.
/// </para>
/// </summary>
public static class StageSheet
{
    public static Sheet Build(IReadOnlyList<CaseStage> stages, IReadOnlyDictionary<Guid, IReadOnlyList<string>> maps,
        IReadOnlyDictionary<Guid, string> labels)
    {
        List<CaseStageRule> own = [.. CaseStageRules.All.Where(rule => rule.OwnColumn)];
        List<string> columns = ["talep_konusu", "asama", "haritadaki_ad", "baslangic_asamasi", "otomatik_ilerler",
            "olumlu_sonraki_asama", "olumlu_is_akisi", "olumlu_sms",
            "olumsuz_sonraki_asama", "olumsuz_is_akisi", "olumsuz_sms",
            "iptal_sonraki_asama", "iptal_is_akisi", "iptal_sms",
            "kullaniciya_atanir"];
        columns.AddRange(own.Select(rule => rule.Column));
        columns.AddRange(["diger_kurallar", "asama_haritasi", "asama_id"]);
        Sheet sheet = new(SheetNames.Stages, [.. columns]);

        Dictionary<Guid, CaseStage> byId = stages.ToDictionary(stage => stage.Id);
        foreach (CaseStage stage in Ordered(stages, byId))
        {
            List<object?> row = [stage.Process, stage.Name, labels.GetValueOrDefault(stage.Id) ?? stage.ShortName, YesNo(stage.Primary), YesNo(stage.Automatic)];
            foreach (string outcome in new[] { StageOutcome.Success, StageOutcome.Failure, StageOutcome.Cancel })
            {
                StageExit? exit = stage.Exits.FirstOrDefault(candidate => candidate.Outcome == outcome);
                row.Add(Next(exit, byId));
                row.Add(exit?.WorkflowName);
                row.Add(exit?.Sms);
            }
            row.Add(YesNo(stage.Fields.ContainsKey(CaseStageRules.AssignedUser)));
            row.AddRange(own.Select(rule => stage.Fields.GetValueOrDefault(rule.Field)));
            row.Add(string.Join(" · ", CaseStageRules.All.Where(rule => !rule.OwnColumn && stage.Fields.ContainsKey(rule.Field))
                .Select(rule => $"{rule.Label}: {stage.Fields[rule.Field]}")));
            row.Add(maps.TryGetValue(stage.Id, out IReadOnlyList<string>? files) ? string.Join("; ", files) : null);
            row.Add(stage.Id);
            sheet.Row([.. row]);
        }
        return sheet;
    }

    /// <summary>
    /// One row per map: the request subject, where it starts, how many stages and workflows its process has, and the
    /// file to open. The reader's first two questions — which process first, and how big — without opening every map.
    /// </summary>
    public static Sheet BuildTopics(RunState state, IReadOnlyList<WorkflowIr> documents)
    {
        Sheet sheet = new(SheetNames.Topics, "talep_konusu", "baslangic_asamasi", "asama_sayisi", "is_akisi_sayisi",
            "is_akislari", "toplam_adim", "asama_haritasi");
        Dictionary<Guid, CaseStage> byId = state.CaseStages.ToDictionary(stage => stage.Id);
        Dictionary<Guid, WorkflowIr> workflows = documents.ToDictionary(document => document.Identity.WorkflowId);
        List<(CaseStage Primary, List<CaseStage> Stages, string File)> topics = [];
        foreach ((Guid primary, string file) in state.StageMapPaths)
        {
            List<CaseStage> on = [.. state.StageMapFiles.Where(entry => entry.Value.Contains(file, StringComparer.Ordinal))
                .Select(entry => byId[entry.Key])];
            topics.Add((byId[primary], on, file));
        }
        foreach ((CaseStage primary, List<CaseStage> on, string file) in topics
            .OrderByDescending(topic => topic.Stages.Count)
            .ThenBy(topic => TurkishFold.Fold(topic.Primary.Process), StringComparer.Ordinal))
        {
            List<StageExit> fired = [.. on.SelectMany(stage => stage.Exits).Where(exit => exit.Workflow is not null)
                .DistinctBy(exit => exit.Workflow)];
            sheet.Row(primary.Process.Length > 0 ? primary.Process : primary.Name, state.StageMapLabels.GetValueOrDefault(primary.Id) ?? primary.ShortName,
                on.Count, fired.Count,
                Sheet.List(fired.Select(exit => exit.WorkflowName ?? exit.Workflow!.Value.ToString("D")).Order(StringComparer.Ordinal)),
                fired.Sum(exit => workflows.TryGetValue(exit.Workflow!.Value, out WorkflowIr? document) ? MigrationPlan.CountSteps(document.Steps) : 0),
                file);
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

    /// <summary>Active stages only, by subject, each subject from its starting stages along their outcomes.</summary>
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
