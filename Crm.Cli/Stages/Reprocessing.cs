using System.Text.Json;
using Crm.Extract.Inventory;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// <c>Run:ReprocessRunId</c>: build a new run from an earlier sealed run's <c>raw/</c> evidence, without the network.
/// The earlier run is only read; its evidence is copied, so the new run folder is complete on its own.
/// </summary>
public static class Reprocessing
{
    public static async Task<IReadOnlyList<WorkflowInventoryRecord>> LoadAsync(RunFolder folder, RunState state, string outputRoot, string runId, ILogger logger, CancellationToken token)
    {
        string source = Path.Combine(outputRoot, "runs", runId);
        string manifestPath = Path.Combine(source, RunFolder.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Run:ReprocessRunId '{runId}' is not a sealed run under {Path.Combine(outputRoot, "runs")}.");
        }

        string raw = Path.Combine(source, "raw");
        foreach (string file in Directory.EnumerateFiles(raw, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = "raw/" + Path.GetRelativePath(raw, file).Replace(Path.DirectorySeparatorChar, '/');
            await folder.CopyVerbatimAsync(file, relative, token);
        }

        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, token));
        JsonElement root = manifest.RootElement;
        state.OrganizationUrl = root.TryGetProperty("organizationUrl", out JsonElement url) ? url.GetString() : null;
        if (root.TryGetProperty("stageCounts", out JsonElement stageCounts))
        {
            foreach (JsonProperty count in stageCounts.EnumerateObject().Where(count => count.Name.StartsWith("xaml.", StringComparison.Ordinal) && count.Name != "xaml.definitions"))
            {
                state.Counts[count.Name] = count.Value.GetInt32();
            }
        }

        List<WorkflowInventoryRecord> records = [];
        foreach (JsonElement record in IrStage.ReadInventory(folder).Values)
        {
            records.Add(WorkflowInventoryRecord.Parse(record));
        }
        records.Sort((left, right) => left.WorkflowId.CompareTo(right.WorkflowId));
        int apiCount = root.TryGetProperty("counts", out JsonElement counts) && counts.ValueKind == JsonValueKind.Object ? counts.GetProperty("apiCount").GetInt32() : records.Count;
        state.Reconciliation = InventoryReconciliation.Evaluate(apiCount, records);
        state.Warnings.AddRange(state.Reconciliation.Warnings);
        state.Records = records;
        state.StagesRun.Add("reprocess:" + runId);
        state.StagesRun.Add("xaml");

        await RetrievalStages.RouteAndDriftAsync(folder, state, XamlEntry.ReadIndex(folder.Root), token);
        logger.LogInformation("Reprocessing run {Source}: {Records} inventory records, no network access", runId, records.Count);
        return records;
    }
}
