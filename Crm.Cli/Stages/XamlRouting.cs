using System.Globalization;
using System.Text;
using Crm.Extract.Inventory;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;

namespace Crm.Cli.Stages;

/// <summary>
/// What is done with the XAML once it is in <c>ham/</c>, however it got there: hand-authored definitions routed to
/// manual review, and each definition compared with its running copy. Both work from the inventory and the stored
/// XAML alone, so an import and a reprocess do exactly the same.
/// </summary>
public static class XamlRouting
{
    public const string ManualReviewIndex = RunPaths.ManualReviewIndex;

    /// <summary>Manual-review routing and drift, in that order.</summary>
    public static async Task RouteAndDriftAsync(RunFolder folder, RunState state, IReadOnlyList<XamlEntry> entries, CancellationToken token)
    {
        await RouteManualReviewAsync(folder, state, entries, token);

        DriftReport drift = DriftAnalyzer.Analyze(state.Records, entries, folder.ReadText);
        state.Drift = drift;
        state.StagesRun.Add(RunStages.Drift);
        state.Counts["drift.pairsCompared"] = drift.PairsCompared;
        state.Counts["drift.structureDiffers"] = drift.Drifted.Count(finding => finding.StructureDiffers);
        state.Counts["drift.draftDefinitions"] = drift.DraftDefinitions.Count;
    }

    /// <summary>§3.3: hand-authored XAML is not parsed. It is copied to manual review and listed, never guessed at.</summary>
    private static async Task RouteManualReviewAsync(RunFolder folder, RunState state, IReadOnlyList<XamlEntry> entries, CancellationToken token)
    {
        HashSet<Guid> withXaml = [.. entries.Select(entry => entry.WorkflowId)];
        List<WorkflowInventoryRecord> manual = [.. state.Records
            .Where(record => record.Type.Raw == WorkflowOptionSets.TypeDefinition && record.IsCrmUiWorkflow == false && withXaml.Contains(record.WorkflowId))
            .OrderBy(record => record.WorkflowId)];

        StringBuilder index = new();
        index.AppendLine("# Elle inceleme — tasarımcıyla yazılmamış").AppendLine();
        index.AppendLine("`iscrmuiworkflow = false`: CRM tasarımcısının açamadığı, elle yazılmış XAML. Ayrıştırılmaz ve BPMN üretilmez; "
            + "çünkü yanlış bir diyagram, kabul edilmiş bir boşluktan daha kötüdür (§3.3).").AppendLine();
        foreach (WorkflowInventoryRecord record in manual)
        {
            await folder.CopyVerbatimAsync(folder.PathOf(XamlEntry.FileFor(record.WorkflowId)), RunPaths.ManualReviewXaml(record.WorkflowId), token);
            index.AppendLine(CultureInfo.InvariantCulture, $"- {record.Name} (`{record.WorkflowId:D}`, {record.Category.Label}, varlık `{record.PrimaryEntity}`)");
        }
        await folder.WriteTextAsync(ManualReviewIndex, index.ToString(), token);
        state.Counts["manualReview"] = manual.Count;
        state.ManualReview = [.. manual.Select(record => record.WorkflowId)];
    }
}
