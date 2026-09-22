using System.Text.Json;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;
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
    /// <summary>
    /// The source run's evidence, copied into this one. A run made before the output was Turkish keeps its evidence
    /// under <c>raw/</c>; it is read from there and written under <c>ham/</c>, so an older run still reprocesses.
    /// </summary>
    private static async Task CopyEvidenceAsync(RunFolder folder, string source, CancellationToken token)
    {
        string raw = Path.Combine(source, RunPaths.Raw);
        if (!Directory.Exists(raw))
        {
            raw = Path.Combine(source, RunPaths.RawEnglish);
        }
        foreach (string file in Directory.EnumerateFiles(raw, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = RunPaths.Raw + "/" + Path.GetRelativePath(raw, file).Replace(Path.DirectorySeparatorChar, '/')
                .Replace("xaml/index.json", "xaml/dizin.json", StringComparison.Ordinal);
            await folder.CopyVerbatimAsync(file, relative, token);
        }
    }

    private static CrmIdentity? UserOf(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("authenticatedUser", out JsonElement user) || user.ValueKind != JsonValueKind.Object
            || Id(user, "userId") is not Guid userId)
        {
            return null;
        }
        return new CrmIdentity(userId, Id(user, "businessUnitId") ?? Guid.Empty, Id(user, "organizationId") ?? Guid.Empty,
            Text(user, "fullName"), Text(user, "domainName"));
    }

    private static Guid? Id(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out Guid parsed)
            ? parsed
            : null;
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static async Task<IReadOnlyList<WorkflowInventoryRecord>> LoadAsync(RunFolder folder, RunState state, string outputRoot, string runId, ILogger logger, CancellationToken token)
    {
        string source = Path.Combine(outputRoot, "runs", runId);
        string manifestPath = Path.Combine(source, RunFolder.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Run:ReprocessRunId '{runId}', {Path.Combine(outputRoot, "runs")} altında mühürlenmiş bir çalıştırma değil.");
        }

        await CopyEvidenceAsync(folder, source, token);

        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, token));
        JsonElement root = manifest.RootElement;
        state.OrganizationUrl = root.TryGetProperty("organizationUrl", out JsonElement url) ? url.GetString() : null;

        // Nothing is fetched here, so there is no WhoAmI to make. Who read the data is a fact of the source run,
        // and carrying it forward keeps the summary honest instead of reading like a failure.
        state.Identity = UserOf(root);
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
        state.StagesRun.Add(RunStages.Reprocess(runId));
        state.StagesRun.Add(RunStages.Xaml);

        await RetrievalStages.RouteAndDriftAsync(folder, state, XamlEntry.ReadIndex(folder.Root), token);
        logger.LogInformation("Reprocessing run {Source}: {Records} inventory records, no network access", runId, records.Count);
        return records;
    }
}
