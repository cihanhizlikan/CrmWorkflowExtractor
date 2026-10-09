namespace Crm.Extract.Preflight;

/// <summary>
/// Who read the data, recorded in the manifest (§2.3): the account signed in to the browser the export ran in, as
/// <c>WhoAmI()</c> named it there.
/// </summary>
public sealed record CrmIdentity(Guid UserId, Guid BusinessUnitId, Guid OrganizationId, string? FullName, string? DomainName);
