using System.Text.Json;

namespace Crm.Extract.Metadata;

public sealed record OptionLabel(int Value, string Label);

/// <summary>The labelled options of one choice column: a picklist, status reason or status.</summary>
public sealed record AttributeOptions(string Attribute, string MetadataType, IReadOnlyList<OptionLabel> Options);

/// <summary>Every choice column of one entity, as written to <c>ham/ust-veri/&lt;entity&gt;.json</c>.</summary>
public sealed record EntityOptionSets(string Entity, IReadOnlyList<AttributeOptions> Attributes);

/// <summary>
/// §3.4: option-set labels, so a condition like <c>new_status eq 100000003</c> can be read. The export asks for them
/// per primary entity; this reads one of its answers.
/// </summary>
public static class OptionSetReader
{
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
