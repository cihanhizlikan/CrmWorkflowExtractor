using System.Text;
using System.Text.Json;
using Crm.Extract.Inventory;
using Crm.Extract.Metadata;
using Crm.Extract.Preflight;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// <c>Run:ImportFile</c>: builds a run from a file saved by <c>tools/crm-browser-export.js</c>, which read CRM through
/// the user's own signed-in browser session. Used because the organization signs in through AD FS and the tool's
/// Windows authentication is refused (plan, 2026-09-22). The export holds the same payloads the tool would have
/// fetched, so this writes the same raw/ layout a network run writes and every later stage runs unchanged.
///
/// <para>
/// Provenance is weaker than a network run's, and the manifest says so: the tool did not make these requests, it
/// copies the export verbatim into <c>raw/</c> and records its hash, the signed-in user and the export time.
/// </para>
/// </summary>
public static class BrowserExportImport
{
    public const string Format = "crm-browser-export/1";
    public const string EvidenceFile = RunPaths.RawBrowserExport;
    public const string SourceBrowserExport = "browser-export";

    public static async Task<IReadOnlyList<WorkflowInventoryRecord>> LoadAsync(RunFolder folder, RunState state, bool requireOrganizationRead, string path, ILogger logger, CancellationToken token)
    {
        string file = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"Run:ImportFile '{file}' bulunamadı.");
        }
        await folder.CopyVerbatimAsync(file, EvidenceFile, token);

        using FileStream stream = File.OpenRead(file);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.GetString() != Format)
        {
            throw new InvalidDataException($"'{file}', tools/crm-browser-export.js ile kaydedilmiş bir {Format} dosyası değil.");
        }
        state.StagesRun.Add(RunStages.Import(Path.GetFileName(file)));
        state.OrganizationUrl = root.GetProperty("webApiRoot").GetString();
        state.Warnings.Add($"Veriler {root.GetProperty("exportedAtUtc").GetString()} tarihinde kullanıcının kendi CRM oturumu üzerinden alınan tarayıcı "
            + "dışa aktarımından içe aktarıldı; istekleri aracın kendisi yapmadı.");

        ReadIdentity(root, state);
        state.Privileges = PrivilegeCheck.Evaluate(root.GetProperty("privileges").GetRawText(), root.GetProperty("userPrivileges").GetRawText());
        state.StagesRun.Add(RunStages.Privileges);
        ExtractionRun.ApplyPrivilegeVerdicts(state, requireOrganizationRead);

        (IReadOnlyList<string> available, IReadOnlyList<string> missing) = WorkflowColumns.Split(root.GetProperty("workflowAttributes").GetRawText());
        state.ColumnsSelected = available;
        state.ColumnsMissing = missing;
        state.StagesRun.Add(RunStages.Columns);
        foreach (string column in missing)
        {
            state.Warnings.Add($"§3.1 listesindeki '{column}' sütunu bu sunucunun workflow varlığında yok; dışa aktarım onu almadı.");
        }

        List<WorkflowInventoryRecord> records = await WriteInventoryAsync(folder, root, token);
        state.StagesRun.Add(RunStages.Inventory);
        ReconciliationResult reconciliation = InventoryReconciliation.Evaluate(root.GetProperty("count").GetInt32(), records);
        state.Reconciliation = reconciliation;
        state.StagesRun.Add(RunStages.Reconciliation);
        state.Warnings.AddRange(reconciliation.Warnings);
        foreach (string failure in reconciliation.Failures)
        {
            state.Fail(ExitCode.RunFailed, failure);
        }
        state.Records = records;
        if (state.Failures.Count > 0)
        {
            logger.LogError("Stopping after the inventory: the imported export fails its own checks.");
            return records;
        }

        IReadOnlyList<XamlEntry> entries = await WriteXamlAsync(folder, state, root, records, token);
        await RetrievalStages.RouteAndDriftAsync(folder, state, entries, token);
        await WriteOptionSetsAsync(folder, state, root, token);
        await WriteProcessStagesAsync(folder, state, root, token);
        logger.LogInformation("Imported {Records} workflow records and {Xaml} XAML files from {File}", records.Count, entries.Count, file);
        return records;
    }

    private static void ReadIdentity(JsonElement root, RunState state)
    {
        JsonElement whoAmI = root.GetProperty("whoAmI");
        JsonElement user = root.GetProperty("user");
        state.Identity = new CrmIdentity(
            whoAmI.GetProperty("UserId").GetGuid(),
            whoAmI.GetProperty("BusinessUnitId").GetGuid(),
            whoAmI.GetProperty("OrganizationId").GetGuid(),
            user.TryGetProperty("fullname", out JsonElement fullName) ? fullName.GetString() : null,
            user.TryGetProperty("domainname", out JsonElement domainName) ? domainName.GetString() : null);
        state.StagesRun.Add(RunStages.Identity);
    }

    private static async Task<List<WorkflowInventoryRecord>> WriteInventoryAsync(RunFolder folder, JsonElement root, CancellationToken token)
    {
        StringBuilder lines = new();
        List<WorkflowInventoryRecord> records = [];
        foreach (JsonElement record in root.GetProperty("workflows").EnumerateArray())
        {
            lines.Append(record.GetRawText().ReplaceLineEndings("")).Append('\n');
            records.Add(WorkflowInventoryRecord.Parse(record));
        }
        await folder.WriteTextAsync(RunPaths.RawWorkflows, lines.ToString(), token);
        return records;
    }

    private static async Task<IReadOnlyList<XamlEntry>> WriteXamlAsync(RunFolder folder, RunState state, JsonElement root, IReadOnlyList<WorkflowInventoryRecord> records, CancellationToken token)
    {
        JsonElement xaml = root.GetProperty("xaml");
        JsonElement errors = root.TryGetProperty("xamlErrors", out JsonElement errorMap) ? errorMap : default;
        List<XamlEntry> entries = [];
        int withoutXaml = 0;
        int failed = 0;
        foreach (WorkflowInventoryRecord record in records
            .Where(record => record.Type.Raw is WorkflowOptionSets.TypeDefinition or WorkflowOptionSets.TypeActivation)
            .OrderBy(record => record.WorkflowId))
        {
            string key = record.WorkflowId.ToString("D");
            string kind = record.Type.Raw == WorkflowOptionSets.TypeDefinition ? XamlEntry.KindDefinition : XamlEntry.KindActivation;
            if (errors.ValueKind == JsonValueKind.Object && errors.TryGetProperty(key, out JsonElement error))
            {
                state.Warnings.Add($"{kind} '{record.Name}' ({key}) XAML dosyası tarayıcı dışa aktarımında okunamadı: {error.GetString()}");
                failed++;
                continue;
            }
            string? text = xaml.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (string.IsNullOrEmpty(text))
            {
                state.Warnings.Add($"{kind} '{record.Name}' ({key}) dışa aktarımda XAML içermiyor.");
                withoutXaml++;
                continue;
            }
            string file = XamlEntry.FileFor(record.WorkflowId);
            await folder.WriteVerbatimAsync(file, text, token);
            RunArtifact artifact = await folder.HashAsync(file, token);
            entries.Add(new XamlEntry(record.WorkflowId, kind, record.VersionNumber, file, artifact.Bytes, artifact.Sha256, SourceBrowserExport));
        }
        await folder.WriteJsonAsync(XamlEntry.IndexFile, entries, token);
        state.Counts["xaml.fetched"] = entries.Count;
        state.Counts["xaml.reused"] = 0;
        state.Counts["xaml.withoutXaml"] = withoutXaml;
        state.Counts["xaml.failed"] = failed;
        state.Counts["xaml.written"] = entries.Count;
        state.StagesRun.Add(RunStages.Xaml);
        return entries;
    }

    private static async Task WriteOptionSetsAsync(RunFolder folder, RunState state, JsonElement root, CancellationToken token)
    {
        int entities = 0;
        if (root.TryGetProperty("optionSets", out JsonElement optionSets) && optionSets.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entity in optionSets.EnumerateObject().OrderBy(entity => entity.Name, StringComparer.Ordinal))
            {
                List<AttributeOptions> attributes = [];
                foreach (JsonProperty body in entity.Value.EnumerateObject())
                {
                    if (body.Value.TryGetProperty("error", out JsonElement error))
                    {
                        state.Warnings.Add($"'{entity.Name}' için {body.Name} seçenek kümesi üst verisi dışa aktarılmadı: {error.GetString()}");
                        continue;
                    }
                    attributes.AddRange(OptionSetMetadataRetriever.Parse(body.Value.GetRawText(), body.Name));
                }
                EntityOptionSets sets = new(entity.Name, [.. attributes.OrderBy(attribute => attribute.Attribute, StringComparer.Ordinal)]);
                await folder.WriteJsonAsync(RunPaths.MetadataFile(entity.Name), sets, token);
                entities++;
            }
        }
        state.Counts["metadata.entities"] = entities;
        state.StagesRun.Add(RunStages.Metadata);
    }

    private static async Task WriteProcessStagesAsync(RunFolder folder, RunState state, JsonElement root, CancellationToken token)
    {
        List<ProcessStage> stages = root.TryGetProperty("processStages", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array
            ? [.. rows.EnumerateArray().Select(ProcessStageRetriever.Parse).OfType<ProcessStage>().OrderBy(stage => stage.ProcessId).ThenBy(stage => stage.StageId)]
            : [];
        await folder.WriteJsonAsync(ProcessStageRetriever.IndexFile, stages, token);
        state.Counts["processStages"] = stages.Count;
        state.StagesRun.Add(RunStages.ProcessStages);
    }
}
