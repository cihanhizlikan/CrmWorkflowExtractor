using Crm.Extract.Inventory;
using Crm.Extract.Preflight;

namespace Crm.Extract.Runs;

public static class RunStatus
{
    public const string Completed = "completed";
    public const string CompletedWithFailures = "completed-with-failures";
    public const string Failed = "failed";
}

public sealed record ManifestUser(Guid? UserId, string? FullName, string? DomainName, Guid? BusinessUnitId, Guid? OrganizationId);

public sealed record ManifestPrivilege(string Name, string Table, string Verdict, string Depth);

/// <summary>
/// §8 run metadata. Property order is declaration order, so the file diffs cleanly between runs.
/// Stages not yet implemented are simply absent from <see cref="StagesRun"/> — never reported as run.
/// </summary>
public sealed record RunManifest(
    int SchemaVersion,
    string RunId,
    string Status,
    string ToolVersion,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    string? Server,
    string? OrganizationUrl,
    string AuthenticationMode,
    ManifestUser? AuthenticatedUser,
    string ConfigurationSha256,
    IReadOnlyList<string> StagesRun,
    IReadOnlyList<string> ColumnsSelected,
    IReadOnlyList<string> ColumnsMissingFromServer,
    IReadOnlyList<ManifestPrivilege> Privileges,
    InventoryCounts? Counts,
    IReadOnlyDictionary<string, int> StageCounts,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RunArtifact> Artifacts)
{
    public static ManifestUser? UserOf(CrmIdentity? identity)
    {
        return identity is null
            ? null
            : new ManifestUser(identity.UserId, identity.FullName, identity.DomainName, identity.BusinessUnitId, identity.OrganizationId);
    }

    public static IReadOnlyList<ManifestPrivilege> PrivilegesOf(IReadOnlyList<PrivilegeFinding> findings)
    {
        return [.. findings.Select(finding => new ManifestPrivilege(finding.Privilege.Name, finding.Privilege.Table, finding.Verdict.ToString(), finding.Depth))];
    }
}
