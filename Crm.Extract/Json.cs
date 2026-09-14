using System.Globalization;
using System.Text.Json;

namespace Crm.Extract;

/// <summary>Tolerant readers for Web API JSON, where a column may be absent, null, or an Int64 written as a string.</summary>
internal static class Json
{
    public static string? OptionalString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static Guid? OptionalGuid(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out Guid parsed)
            ? parsed
            : null;
    }

    public static int? OptionalInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    public static long? OptionalLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    public static bool? OptionalBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    /// <summary>The server's display label for a column, from the FormattedValue annotation.</summary>
    public static string? FormattedValue(JsonElement element, string property)
    {
        return OptionalString(element, property + "@OData.Community.Display.V1.FormattedValue");
    }
}
