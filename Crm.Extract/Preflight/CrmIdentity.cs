using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Preflight;

/// <summary>Who the run is authenticated as, recorded in the manifest (§2.3).</summary>
public sealed record CrmIdentity(Guid UserId, Guid BusinessUnitId, Guid OrganizationId, string? FullName, string? DomainName)
{
    /// <summary><c>WhoAmI()</c> then the user's own names. Both are GET.</summary>
    public static async Task<CrmIdentity> ResolveAsync(CrmHttpClient client, CancellationToken token)
    {
        CrmResponse whoAmI = await client.GetAsync("WhoAmI()", CrmPreferences.None, token);
        Guid userId;
        Guid businessUnitId;
        Guid organizationId;
        using (JsonDocument document = JsonDocument.Parse(whoAmI.Body))
        {
            JsonElement root = document.RootElement;
            userId = root.GetProperty("UserId").GetGuid();
            businessUnitId = root.GetProperty("BusinessUnitId").GetGuid();
            organizationId = root.GetProperty("OrganizationId").GetGuid();
        }

        CrmResponse user = await client.GetAsync($"systemusers({userId:D})?$select=fullname,domainname", CrmPreferences.None, token);
        using JsonDocument userDocument = JsonDocument.Parse(user.Body);
        return new CrmIdentity(userId, businessUnitId, organizationId,
            Json.OptionalString(userDocument.RootElement, "fullname"),
            Json.OptionalString(userDocument.RootElement, "domainname"));
    }
}
