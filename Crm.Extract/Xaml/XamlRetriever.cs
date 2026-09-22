using System.Text.Json;
using Crm.Extract.Http;
using Crm.Extract.Inventory;
using Crm.Extract.Runs;
using Microsoft.Extensions.Logging;

namespace Crm.Extract.Xaml;

public sealed record XamlPass(IReadOnlyList<XamlEntry> Entries, int Fetched, int Reused, int WithoutXaml, int Failed);

/// <summary>
/// Second pass of §3.5: the <c>xaml</c> of every definition and activation, one record per request, written straight
/// to <c>raw/xaml/</c> unmodified. Templates are skipped. A record that fails is warned and skipped — a failure on
/// workflow 200 does not discard the 199 before it — while authentication and deployment faults still stop the run.
/// </summary>
public sealed class XamlRetriever(CrmHttpClient client, ILogger logger)
{
    /// <summary>The query this pass issues; the run's evidence recorder recognises it to keep large bodies out of memory.</summary>
    public const string SelectMarker = "$select=xaml";

    public async Task<XamlPass> RetrieveAsync(
        RunFolder folder,
        IReadOnlyList<WorkflowInventoryRecord> records,
        IReadOnlyDictionary<(Guid WorkflowId, long VersionNumber), ReusableXaml> reusable,
        List<string> warnings,
        CancellationToken token)
    {
        List<XamlEntry> entries = [];
        int fetched = 0;
        int reused = 0;
        int withoutXaml = 0;
        int failed = 0;
        List<WorkflowInventoryRecord> wanted = [.. records
            .Where(record => record.Type.Raw is WorkflowOptionSets.TypeDefinition or WorkflowOptionSets.TypeActivation)
            .OrderBy(record => record.WorkflowId)];

        foreach (WorkflowInventoryRecord record in wanted)
        {
            string kind = record.Type.Raw == WorkflowOptionSets.TypeDefinition ? XamlEntry.KindDefinition : XamlEntry.KindActivation;
            string file = XamlEntry.FileFor(record.WorkflowId);

            if (record.VersionNumber is long version && reusable.TryGetValue((record.WorkflowId, version), out ReusableXaml? prior)
                && string.Equals(RunFolder.Sha256Of(prior.AbsolutePath), prior.Entry.Sha256, StringComparison.Ordinal))
            {
                await folder.CopyVerbatimAsync(prior.AbsolutePath, file, token);
                entries.Add(await EntryAsync(folder, record, kind, file, "reused:" + prior.RunId, token));
                reused++;
                continue;
            }

            string? xaml;
            try
            {
                CrmResponse response = await client.GetAsync($"workflows({record.WorkflowId:D})?{SelectMarker}", CrmPreferences.None, token);
                using JsonDocument document = JsonDocument.Parse(response.Body);
                xaml = Json.OptionalString(document.RootElement, "xaml");
            }
            catch (Exception error) when (error is CrmRequestException or JsonException)
            {
                warnings.Add($"{kind} '{record.Name}' ({record.WorkflowId:D}) XAML dosyası alınamadı: {error.Message}");
                failed++;
                continue;
            }

            if (string.IsNullOrEmpty(xaml))
            {
                warnings.Add($"{kind} '{record.Name}' ({record.WorkflowId:D}) XAML içermiyor.");
                withoutXaml++;
                continue;
            }
            await folder.WriteVerbatimAsync(file, xaml, token);
            entries.Add(await EntryAsync(folder, record, kind, file, XamlEntry.SourceFetched, token));
            fetched++;
            if (fetched % 25 == 0)
            {
                logger.LogInformation("XAML: {Fetched} fetched, {Reused} reused of {Total}", fetched, reused, wanted.Count);
            }
        }

        await folder.WriteJsonAsync(XamlEntry.IndexFile, entries, token);
        logger.LogInformation("XAML done: {Fetched} fetched, {Reused} reused, {Without} without XAML, {Failed} failed", fetched, reused, withoutXaml, failed);
        return new XamlPass(entries, fetched, reused, withoutXaml, failed);
    }

    private static async Task<XamlEntry> EntryAsync(RunFolder folder, WorkflowInventoryRecord record, string kind, string file, string source, CancellationToken token)
    {
        RunArtifact artifact = await folder.HashAsync(file, token);
        return new XamlEntry(record.WorkflowId, kind, record.VersionNumber, file, artifact.Bytes, artifact.Sha256, source);
    }
}
