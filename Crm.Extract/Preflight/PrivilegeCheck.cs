using System.Text;
using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Preflight;

public enum PrivilegeVerdict
{
    /// <summary>Held at organization (Global) depth.</summary>
    Sufficient,

    /// <summary>Held, but below Global — the §2.4 trap: reads return a fraction of the data, silently.</summary>
    Insufficient,

    /// <summary>Not held through any role.</summary>
    Missing,

    /// <summary>No privilege of that name exists in this organization, so it cannot be checked by name.</summary>
    Unverifiable
}

public sealed record RequiredPrivilege(string Name, string Table);

public sealed record PrivilegeFinding(RequiredPrivilege Privilege, PrivilegeVerdict Verdict, string Depth);

/// <summary>
/// The check §2.4 actually needs. Count reconciliation cannot detect insufficient read depth: <c>$count</c> and the
/// retrieval pass through the SAME security filter, so a user-level account counts and retrieves the same fraction
/// and reconciles cleanly. This asks the server what depth the user holds instead.
/// </summary>
public static class PrivilegeCheck
{
    /// <summary>Handout §2.4. Names are CRM's privilege naming convention; any absent from the org is reported, not assumed.</summary>
    public static readonly IReadOnlyList<RequiredPrivilege> Required =
    [
        new("prvReadWorkflow", "Process"),
        new("prvReadProcessStage", "Process Stage"),
        new("prvReadAsyncOperation", "System Job"),
        new("prvReadPluginAssembly", "Plug-in Assembly"),
        new("prvReadPluginType", "Plug-in Type"),
        new("prvReadSdkMessageProcessingStep", "SDK Message Processing Step")
    ];

    private static readonly string[] DepthOrder = ["Basic", "Local", "Deep", "Global"];

    public static async Task<IReadOnlyList<PrivilegeFinding>> RunAsync(CrmHttpClient client, Guid userId, CancellationToken token)
    {
        StringBuilder filter = new();
        foreach (RequiredPrivilege privilege in Required)
        {
            if (filter.Length > 0)
            {
                filter.Append(" or ");
            }
            filter.Append("name eq '").Append(privilege.Name).Append('\'');
        }
        CrmResponse privileges = await client.GetAsync($"privileges?$select=privilegeid,name&$filter={Uri.EscapeDataString(filter.ToString())}", CrmPreferences.None, token);
        CrmResponse held = await client.GetAsync($"systemusers({userId:D})/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()", CrmPreferences.None, token);
        return Evaluate(privileges.Body, held.Body);
    }

    /// <summary>Pure evaluation over the two response bodies, so every verdict is testable without a server.</summary>
    public static IReadOnlyList<PrivilegeFinding> Evaluate(string privilegesBody, string retrieveUserPrivilegesBody)
    {
        Dictionary<string, Guid> idByName = new(StringComparer.OrdinalIgnoreCase);
        using (JsonDocument document = JsonDocument.Parse(privilegesBody))
        {
            foreach (JsonElement row in document.RootElement.GetProperty("value").EnumerateArray())
            {
                string? name = Json.OptionalString(row, "name");
                Guid? id = Json.OptionalGuid(row, "privilegeid");
                if (name is not null && id is Guid privilegeId)
                {
                    idByName[name] = privilegeId;
                }
            }
        }

        Dictionary<Guid, int> bestDepth = [];
        using (JsonDocument document = JsonDocument.Parse(retrieveUserPrivilegesBody))
        {
            foreach (JsonElement row in document.RootElement.GetProperty("RolePrivileges").EnumerateArray())
            {
                Guid? id = Json.OptionalGuid(row, "PrivilegeId");
                int depth = DepthRank(row.GetProperty("Depth"));
                if (id is Guid privilegeId && (!bestDepth.TryGetValue(privilegeId, out int existing) || depth > existing))
                {
                    bestDepth[privilegeId] = depth;
                }
            }
        }

        List<PrivilegeFinding> findings = [];
        foreach (RequiredPrivilege privilege in Required)
        {
            if (!idByName.TryGetValue(privilege.Name, out Guid privilegeId))
            {
                findings.Add(new PrivilegeFinding(privilege, PrivilegeVerdict.Unverifiable, "-"));
                continue;
            }
            if (!bestDepth.TryGetValue(privilegeId, out int depth))
            {
                findings.Add(new PrivilegeFinding(privilege, PrivilegeVerdict.Missing, "none"));
                continue;
            }
            string label = depth >= 0 && depth < DepthOrder.Length ? DepthOrder[depth] : "Unknown";
            PrivilegeVerdict verdict = depth == DepthOrder.Length - 1 ? PrivilegeVerdict.Sufficient : PrivilegeVerdict.Insufficient;
            findings.Add(new PrivilegeFinding(privilege, verdict, label));
        }
        return findings;
    }

    /// <summary>PrivilegeDepth serializes as its name ("Global"); tolerate the numeric form (Basic=0 … Global=3) too.</summary>
    private static int DepthRank(JsonElement depth)
    {
        if (depth.ValueKind == JsonValueKind.Number && depth.TryGetInt32(out int number))
        {
            return number;
        }
        string text = depth.GetString() ?? "";
        return Array.FindIndex(DepthOrder, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
    }
}
