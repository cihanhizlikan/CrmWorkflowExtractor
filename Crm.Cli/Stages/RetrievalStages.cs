using System.Globalization;
using System.Text;
using Crm.Cli.Configuration;
using Crm.Extract.Http;
using Crm.Extract.Inventory;
using Crm.Extract.Metadata;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>M2: everything the offline stages need from the server beyond the inventory.</summary>
public static class RetrievalStages
{
    public const string ManualReviewIndex = RunPaths.ManualReviewIndex;

    public static async Task RunAsync(RunFolder folder, RunState state, CrmHttpClient client, ExtractorSettings settings, ILogger logger, CancellationToken token)
    {
        string outputRoot = settings.Output.Value.ResolvedRoot();

        IReadOnlyDictionary<(Guid, long), ReusableXaml> reusable = PriorRuns.FindReusableXaml(outputRoot, folder.RunId);
        XamlPass xaml = await new XamlRetriever(client, logger).RetrieveAsync(folder, state.Records, reusable, state.Warnings, token);
        state.StagesRun.Add(RunStages.Xaml);
        state.Counts["xaml.fetched"] = xaml.Fetched;
        state.Counts["xaml.reused"] = xaml.Reused;
        state.Counts["xaml.withoutXaml"] = xaml.WithoutXaml;
        state.Counts["xaml.failed"] = xaml.Failed;
        state.Counts["xaml.written"] = xaml.Entries.Count;

        await RouteAndDriftAsync(folder, state, xaml.Entries, token);

        await RetrieveOptionSetsAsync(folder, state, client, outputRoot, token);

        try
        {
            // The code registered in CRM: which custom activity is still registered, what else reaches outside, and
            // the addresses written inside the assemblies a workflow's activities come from (§3.5).
            PluginRegistry plugins = await new PluginRegistryRetriever(client, settings.Crm.Value.PageSize).RetrieveAsync(token);
            await folder.WriteJsonAsync(PluginRegistryRetriever.IndexFile, plugins, token);
            state.Counts["plugins.assemblies"] = plugins.Assemblies.Count;
            state.Counts["plugins.steps"] = plugins.Steps.Count;
            state.StagesRun.Add(RunStages.Plugins);
        }
        catch (CrmRequestException error)
        {
            state.Warnings.Add("Eklenti kayıtları alınamadı; özel etkinliklerin derlemelerindeki adresler görünmeyecek: " + error.Message);
        }

        try
        {
            IReadOnlyList<ProcessStage> stages = await new ProcessStageRetriever(client, settings.Crm.Value.PageSize).RetrieveAsync(token);
            await folder.WriteJsonAsync(ProcessStageRetriever.IndexFile, stages, token);
            state.Counts["processStages"] = stages.Count;
            state.StagesRun.Add(RunStages.ProcessStages);
        }
        catch (CrmRequestException error)
        {
            state.Warnings.Add("İş süreci akışı aşamaları alınamadı; bu akışların aşamaları adsız kalacak: " + error.Message);
        }
    }

    /// <summary>Manual-review routing and drift: both work from the inventory and raw/xaml alone, so a reprocessed run repeats them.</summary>
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

    /// <summary>§3.3: hand-authored XAML is not parsed. It is copied to manual-review/ and listed, never guessed at.</summary>
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

    private static async Task RetrieveOptionSetsAsync(RunFolder folder, RunState state, CrmHttpClient client, string outputRoot, CancellationToken token)
    {
        OptionSetMetadataRetriever retriever = new(client);
        int entities = 0;
        foreach (string entity in state.Records
            .Select(record => record.PrimaryEntity)
            .OfType<string>()
            .Where(entity => entity.Length > 0 && !string.Equals(entity, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal))
        {
            try
            {
                await retriever.GetAsync(outputRoot, entity, token);
                await folder.CopyVerbatimAsync(OptionSetMetadataRetriever.CachePath(outputRoot, entity), RunPaths.MetadataFile(entity), token);
                entities++;
            }
            catch (CrmRequestException error)
            {
                state.Warnings.Add($"'{entity}' varlığının seçenek kümesi üst verisi alınamadı; koşullarında yalnızca ham değerler görünecek: {error.Message}");
            }
        }
        state.Counts["metadata.entities"] = entities;
        state.StagesRun.Add(RunStages.Metadata);
    }
}
