using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Preflight;

/// <summary>
/// The §3.1 column list, checked against the live entity metadata before it is used. A <c>$select</c> naming a
/// column that does not exist fails the whole query, and several columns in the handout's list are taken from
/// current documentation rather than the 8.2 reference — so absent ones are excluded and reported, not guessed.
/// </summary>
public static class WorkflowColumns
{
    /// <summary>
    /// Every §3.1 column except <c>xaml</c>, which the second pass (M2) fetches per record.
    /// <para>
    /// <b>Deviation from the handout:</b> it lists <c>parentworkflowid</c> and <c>activeworkflowid</c> bare. Both are
    /// lookups, and the Web API exposes a lookup in <c>$select</c> only as <c>_name_value</c>; the bare name is a
    /// navigation property and fails the entire query.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Inventory =
    [
        "workflowid", "name", "uniquename", "description", "category", "type", "mode", "scope", "statecode", "statuscode",
        "primaryentity", "ondemand", "subprocess", "asyncautodelete", "istransacted", "syncworkflowlogonfailure", "rank",
        "runas", "triggeroncreate", "triggerondelete", "triggeronupdateattributelist", "createstage", "updatestage",
        "deletestage", "businessprocesstype", "processorder", "languagecode", "iscrmuiworkflow", "ismanaged",
        "componentstate", "_parentworkflowid_value", "_activeworkflowid_value", "createdon", "modifiedon",
        "_createdby_value", "_modifiedby_value", "_ownerid_value", "versionnumber"
    ];

    /// <summary>A lookup column <c>_x_value</c> is the attribute <c>x</c> in metadata.</summary>
    public static string AttributeNameOf(string column)
    {
        return column.StartsWith('_') && column.EndsWith("_value", StringComparison.Ordinal)
            ? column[1..^"_value".Length]
            : column;
    }

    public static async Task<(IReadOnlyList<string> Available, IReadOnlyList<string> Missing)> ResolveAsync(CrmHttpClient client, CancellationToken token)
    {
        CrmResponse response = await client.GetAsync("EntityDefinitions(LogicalName='workflow')/Attributes?$select=LogicalName", CrmPreferences.None, token);
        return Split(response.Body);
    }

    public static (IReadOnlyList<string> Available, IReadOnlyList<string> Missing) Split(string attributesBody)
    {
        HashSet<string> attributes = new(StringComparer.OrdinalIgnoreCase);
        using (JsonDocument document = JsonDocument.Parse(attributesBody))
        {
            foreach (JsonElement row in document.RootElement.GetProperty("value").EnumerateArray())
            {
                string? name = Json.OptionalString(row, "LogicalName");
                if (name is not null)
                {
                    attributes.Add(name);
                }
            }
        }
        List<string> available = [.. Inventory.Where(column => attributes.Contains(AttributeNameOf(column)))];
        List<string> missing = [.. Inventory.Where(column => !attributes.Contains(AttributeNameOf(column)))];
        return (available, missing);
    }
}
