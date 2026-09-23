using System.Globalization;
using System.Text.Json;
using System.Xml;
using Crm.Extract.Inventory;
using Crm.Extract.Metadata;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Ir.Reports;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>
/// M3: raw XAML → <c>ir/&lt;workflowid&gt;.json</c>, plus <c>parse-coverage.md</c> and <c>sensitive-literals.md</c>.
/// Reads only files in the run folder, never the network, so it can be re-run over an existing run's evidence.
/// </summary>
public static class IrStage
{
    public static async Task<IReadOnlyList<WorkflowIr>> RunAsync(RunFolder folder, RunState state, DateTimeOffset extractedAt, ILogger logger, CancellationToken token)
    {
        Dictionary<Guid, JsonElement> inventory = ReadInventory(folder);
        OptionLabels labels = ReadLabels(folder);
        XamlWorkflowParser parser = new(labels);

        List<WorkflowIr> documents = [];
        List<WorkflowCoverage> coverage = [];
        Literals literals = new();
        int failures = 0;
        int orphaned = 0;
        foreach (XamlEntry entry in XamlEntry.ReadIndex(folder.Root)
            .Where(entry => entry.Kind == XamlEntry.KindDefinition)
            .OrderBy(entry => entry.WorkflowId))
        {
            if (!inventory.TryGetValue(entry.WorkflowId, out JsonElement record))
            {
                state.Warnings.Add($"{entry.File} XAML dosyasının envanter kaydı yok; atlandı.");
                orphaned++;
                continue;
            }
            WorkflowIdentity identity = Identity(record);
            if (identity.IsCrmUiWorkflow == false)
            {
                continue;
            }

            string xaml = folder.ReadText(entry.File);
            ParseResult result;
            try
            {
                result = parser.Parse(identity.WorkflowId, xaml);
            }
            catch (Exception error) when (error is XmlException or InvalidDataException)
            {
                state.Warnings.Add($"'{identity.Name}' ({identity.WorkflowId:D}) XAML dosyası ayrıştırılamadı: {error.Message}");
                failures++;
                continue;
            }

            WorkflowIr document = Document(identity, record, result, entry, extractedAt, state.ToolVersion);
            await folder.WriteJsonAsync(RunPaths.IrFile(identity.WorkflowId), document, token);
            documents.Add(document);
            coverage.Add(new WorkflowCoverage(identity.WorkflowId, identity.Name, result.Coverage));
            literals.Add(identity, entry.File, result);
        }

        await literals.PublishAsync(folder, state, token);
        state.Sheets[Reports.SheetNames.Unmapped] = Reports.QualitySheets.Unmapped(coverage);
        state.Counts["ir.documents"] = documents.Count;
        state.Counts["ir.noInventoryRecord"] = orphaned;
        state.Counts["xaml.definitions"] = XamlEntry.ReadIndex(folder.Root).Count(entry => entry.Kind == XamlEntry.KindDefinition);
        state.Counts["ir.parseFailed"] = failures;
        state.Counts["ir.workflowsWithUnmapped"] = coverage.Count(workflow => workflow.Observations.Any(observation => observation.Status == CoverageStatus.Unmapped));
        state.UnmappedSteps = coverage.ToDictionary(workflow => workflow.WorkflowId,
            workflow => workflow.Observations.Count(observation => observation.Status == CoverageStatus.Unmapped));
        state.StagesRun.Add(RunStages.Ir);
        logger.LogInformation("IR: {Documents} documents, {Failures} parse failures, {Sensitive} sensitive literal findings",
            documents.Count, failures, state.Counts["sensitive.findings"]);
        return documents;
    }

    /// <summary>
    /// What the literals of every definition yield. Two reports walk the same text: the sensitive findings, which
    /// are restricted and never carry the value, and the addresses, which are delivered and do. They are gathered
    /// in one pass and published together, so neither can quietly read a narrower set of literals than the other —
    /// which is exactly how the delivered address page once came out empty while the restricted one had findings.
    /// </summary>
    private sealed class Literals
    {
        private readonly List<SensitiveFinding> _findings = [];
        private readonly List<Reports.ExternalAddress> _addresses = [];

        public void Add(WorkflowIdentity identity, string sourceFile, ParseResult result)
        {
            _findings.AddRange(SensitiveLiteralScanner.Scan(identity.WorkflowId, identity.Name, sourceFile, result.Literals));
            _addresses.AddRange(Reports.ExternalSystems.Find(identity.Name, result.Literals));
        }

        public async Task PublishAsync(RunFolder folder, RunState state, CancellationToken token)
        {
            await folder.WriteTextAsync(RunPaths.SensitiveLiterals, SensitiveLiteralScanner.Markdown(_findings), token);
            state.Counts["sensitive.findings"] = _findings.Count;
            state.SensitiveWorkflows = _findings.Select(finding => finding.WorkflowId).ToHashSet();
            state.Addresses = _addresses;
        }
    }

    private static WorkflowIr Document(WorkflowIdentity identity, JsonElement record, ParseResult result, XamlEntry entry, DateTimeOffset extractedAt, string toolVersion)
    {
        string extracted = extractedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return new WorkflowIr(identity, Trigger(record), result.Steps, result.Dependencies, result.DataTouched, result.Warnings,
            new IrProvenance(entry.File, entry.Sha256, extracted, toolVersion));
    }

    public static Dictionary<Guid, JsonElement> ReadInventory(RunFolder folder)
    {
        Dictionary<Guid, JsonElement> records = [];
        if (!folder.Exists(RunPaths.RawWorkflows))
        {
            return records;
        }
        foreach (string line in folder.ReadText(RunPaths.RawWorkflows).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("workflowid", out JsonElement id) && id.TryGetGuid(out Guid workflowId))
            {
                records[workflowId] = document.RootElement.Clone();
            }
        }
        return records;
    }

    private static OptionLabels ReadLabels(RunFolder folder)
    {
        OptionLabels labels = new();
        string directory = folder.PathOf(RunPaths.RawMetadata);
        if (!Directory.Exists(directory))
        {
            return labels;
        }
        foreach (string file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            EntityOptionSets? sets = JsonSerializer.Deserialize<EntityOptionSets>(File.ReadAllText(file), RunFolder.JsonOptions);
            foreach (AttributeOptions attribute in sets?.Attributes ?? [])
            {
                foreach (OptionLabel option in attribute.Options)
                {
                    labels.Add(sets!.Entity, attribute.Attribute, option.Value, option.Label);
                }
            }
        }
        return labels;
    }

    private static WorkflowIdentity Identity(JsonElement record)
    {
        return new WorkflowIdentity(
            record.GetProperty("workflowid").GetGuid(),
            Text(record, "name") ?? "",
            Text(record, "uniquename"),
            Option(record, "category"),
            Option(record, "type"),
            Text(record, "primaryentity"),
            Option(record, "mode"),
            Option(record, "scope"),
            Option(record, "statecode"),
            Bool(record, "subprocess"),
            Bool(record, "ondemand"),
            record.TryGetProperty("_ownerid_value", out JsonElement owner) && owner.TryGetGuid(out Guid ownerId) ? ownerId : null,
            Text(record, "createdon"),
            Text(record, "modifiedon"),
            record.TryGetProperty("versionnumber", out JsonElement version) && long.TryParse(version.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : null,
            Bool(record, "iscrmuiworkflow"))
        {
            IsManaged = Bool(record, "ismanaged")
        };
    }

    private static WorkflowTrigger Trigger(JsonElement record)
    {
        string updateFields = Text(record, "triggeronupdateattributelist") ?? "";
        return new WorkflowTrigger(
            Bool(record, "triggeroncreate") == true,
            Bool(record, "triggerondelete") == true,
            [.. updateFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal)],
            OptionOrNull(record, "createstage"),
            OptionOrNull(record, "updatestage"),
            OptionOrNull(record, "deletestage"),
            Option(record, "runas"),
            Bool(record, "ondemand") == true);
    }

    private static string? Text(JsonElement record, string property)
    {
        return record.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool? Bool(JsonElement record, string property)
    {
        return record.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }

    private static int? Int(JsonElement record, string property)
    {
        return record.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
    }

    private static string Option(JsonElement record, string column)
    {
        return WorkflowOptionSets.Describe(column, Int(record, column)).Label;
    }

    private static string? OptionOrNull(JsonElement record, string column)
    {
        int? raw = Int(record, column);
        return raw is null ? null : WorkflowOptionSets.Describe(column, raw).Label;
    }
}
