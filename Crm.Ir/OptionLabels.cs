using System.Globalization;

namespace Crm.Ir;

/// <summary>Option-set labels by entity and attribute, used to resolve a raw integer in a condition or an update.</summary>
public sealed class OptionLabels
{
    private readonly Dictionary<(string Entity, string Attribute), Dictionary<int, string>> _labels = [];

    public static readonly OptionLabels Empty = new();

    public void Add(string entity, string attribute, int value, string label)
    {
        (string, string) key = (entity.ToLowerInvariant(), attribute.ToLowerInvariant());
        if (!_labels.TryGetValue(key, out Dictionary<int, string>? options))
        {
            options = [];
            _labels[key] = options;
        }
        options[value] = label;
    }

    /// <summary>The label for a raw value, or null when the value is not an integer or no label is known.</summary>
    public string? Resolve(string? entity, string? attribute, string raw)
    {
        if (entity is null || attribute is null
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || !_labels.TryGetValue((entity.ToLowerInvariant(), attribute.ToLowerInvariant()), out Dictionary<int, string>? options))
        {
            return null;
        }
        return options.TryGetValue(value, out string? label) ? label : null;
    }
}
