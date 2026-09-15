using System.Text.Json;
using Crm.Extract.Http;
using Crm.Extract.Runs;

namespace Crm.Extract.Metadata;

public sealed record OptionLabel(int Value, string Label);

/// <summary>The labelled options of one choice column: a picklist, status reason or status.</summary>
public sealed record AttributeOptions(string Attribute, string MetadataType, IReadOnlyList<OptionLabel> Options);

/// <summary>Every choice column of one entity, as cached to <c>out/cache/metadata/&lt;entity&gt;.json</c>.</summary>
public sealed record EntityOptionSets(string Entity, IReadOnlyList<AttributeOptions> Attributes);

/// <summary>
/// §3.4: option-set labels so a condition like <c>new_status eq 100000003</c> can be read. Retrieved per primary
/// entity, cached on disk because metadata is large and does not change between runs, and copied into each run.
/// </summary>
public sealed class OptionSetMetadataRetriever(CrmHttpClient client)
{
    private static readonly string[] MetadataTypes = ["PicklistAttributeMetadata", "StatusAttributeMetadata", "StateAttributeMetadata"];

    public static string CachePath(string outputRoot, string entity)
    {
        return Path.Combine(outputRoot, "cache", "metadata", entity + ".json");
    }

    public async Task<EntityOptionSets> GetAsync(string outputRoot, string entity, CancellationToken token)
    {
        string cache = CachePath(outputRoot, entity);
        if (File.Exists(cache))
        {
            EntityOptionSets? cached = JsonSerializer.Deserialize<EntityOptionSets>(await File.ReadAllTextAsync(cache, token), RunFolder.JsonOptions);
            if (cached is not null)
            {
                return cached;
            }
        }

        List<AttributeOptions> attributes = [];
        foreach (string type in MetadataTypes)
        {
            string path = $"EntityDefinitions(LogicalName='{entity}')/Attributes/Microsoft.Dynamics.CRM.{type}"
                + "?$select=LogicalName&$expand=OptionSet($select=Options)";
            CrmResponse response = await client.GetAsync(path, CrmPreferences.None, token);
            attributes.AddRange(Parse(response.Body, type));
        }
        EntityOptionSets result = new(entity, [.. attributes.OrderBy(attribute => attribute.Attribute, StringComparer.Ordinal)]);

        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(result, RunFolder.JsonOptions) + "\n", token);
        return result;
    }

    public static IReadOnlyList<AttributeOptions> Parse(string body, string metadataType)
    {
        List<AttributeOptions> attributes = [];
        using JsonDocument document = JsonDocument.Parse(body);
        foreach (JsonElement row in document.RootElement.GetProperty("value").EnumerateArray())
        {
            string? name = Json.OptionalString(row, "LogicalName");
            if (name is null || !row.TryGetProperty("OptionSet", out JsonElement optionSet) || optionSet.ValueKind != JsonValueKind.Object
                || !optionSet.TryGetProperty("Options", out JsonElement options) || options.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            List<OptionLabel> labels = [];
            foreach (JsonElement option in options.EnumerateArray())
            {
                int? value = Json.OptionalInt(option, "Value");
                string? label = option.TryGetProperty("Label", out JsonElement labelElement)
                    && labelElement.TryGetProperty("UserLocalizedLabel", out JsonElement localized)
                    && localized.ValueKind == JsonValueKind.Object
                    ? Json.OptionalString(localized, "Label")
                    : null;
                if (value is int number)
                {
                    labels.Add(new OptionLabel(number, label ?? ""));
                }
            }
            attributes.Add(new AttributeOptions(name, metadataType, [.. labels.OrderBy(option => option.Value)]));
        }
        return attributes;
    }
}
