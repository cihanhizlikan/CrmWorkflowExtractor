using System.Text.Json;
using Crm.Ir.Model;

namespace Crm.Ir.Stages;

/// <summary>
/// Reads the stage records the export kept verbatim into <see cref="CaseStage"/>s.
///
/// <para>
/// The field names are Pensionsoft's, read off this organisation's own metadata on 2026-10-09 rather than guessed:
/// <c>ps_nextstepfor{success,unsuccess,cancel}id</c> for the stage that follows, <c>ps_wffor…id</c> for the workflow
/// fired, <c>ps_smstextfor…</c> for the message sent, <c>ps_primarystep</c>, <c>ps_isautostep</c>, and
/// <c>ps_categoryid</c> for the case subcategory the stage belongs to.
/// </para>
/// </summary>
public static class CaseStageReader
{
    private const string Formatted = "@OData.Community.Display.V1.FormattedValue";

    /// <summary>Every row that carries an id. Rows are taken in the order given; ordering is the reader's business.</summary>
    public static IReadOnlyList<CaseStage> Read(IEnumerable<JsonElement> rows)
    {
        return [.. rows.Select(Stage).OfType<CaseStage>()];
    }

    private static CaseStage? Stage(JsonElement row)
    {
        if (Guid(row, "ps_stepid") is not Guid id)
        {
            return null;
        }
        string name = Text(row, "ps_name") ?? "";
        return new CaseStage(
            id,
            name,
            Text(row, "ps_stepname") is string shortName && shortName.Length > 0 ? shortName : name,
            Guid(row, "_ps_categoryid_value"),
            Label(row, "_ps_categoryid_value") ?? "",
            Flag(row, "ps_primarystep"),
            Flag(row, "ps_isautostep"),
            row.TryGetProperty("statecode", out JsonElement state) && state.ValueKind == JsonValueKind.Number && state.GetInt32() == 0,
            [.. Exits(row)],
            Fields(row));
    }

    private static IEnumerable<StageExit> Exits(JsonElement row)
    {
        (string Outcome, string Suffix)[] outcomes =
            [(StageOutcome.Success, "success"), (StageOutcome.Failure, "unsuccess"), (StageOutcome.Cancel, "cancel")];
        foreach ((string outcome, string suffix) in outcomes)
        {
            string next = $"_ps_nextstepfor{suffix}id_value";
            string workflow = $"_ps_wffor{suffix}id_value";
            string? sms = Text(row, $"ps_smstextfor{suffix}");
            if (Guid(row, next) is null && Guid(row, workflow) is null && sms is null)
            {
                continue;
            }
            yield return new StageExit(outcome, Guid(row, next), Label(row, next), Guid(row, workflow), Label(row, workflow), sms);
        }
    }

    /// <summary>
    /// Every field as display text, keyed by logical name: a lookup's <c>_x_value</c> is filed under <c>x</c>, and a
    /// value CRM sent a label for is shown by that label. A field with no value is not listed at all.
    /// </summary>
    private static Dictionary<string, string> Fields(JsonElement row)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (JsonProperty property in row.EnumerateObject())
        {
            if (property.Name.Contains('@', StringComparison.Ordinal) || property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }
            string logical = property.Name.StartsWith('_') && property.Name.EndsWith("_value", StringComparison.Ordinal)
                ? property.Name[1..^"_value".Length]
                : property.Name;
            string? value = Label(row, property.Name) ?? property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "Evet",
                JsonValueKind.False => "Hayır",
                _ => property.Value.GetRawText()
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields[logical] = value;
            }
        }
        return fields;
    }

    private static string? Label(JsonElement row, string name)
    {
        return row.TryGetProperty(name + Formatted, out JsonElement label) && label.ValueKind == JsonValueKind.String ? label.GetString() : null;
    }

    private static string? Text(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is string text
            && text.Trim().Length > 0 ? text : null;
    }

    private static Guid? Guid(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            && System.Guid.TryParse(value.GetString(), out Guid id) ? id : null;
    }

    private static bool Flag(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }
}
