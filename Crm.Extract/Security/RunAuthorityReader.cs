using System.Text.Json;

namespace Crm.Extract.Security;

/// <summary>
/// A security role that lets its holders start a process by hand, and who holds it. <see cref="RunDepthMask"/> and
/// <see cref="ProcessDepthMask"/> are CRM's privilege depths (1 Basic, 2 Local, 4 Deep, 8 Global): the first says
/// how far the holder's right to run a process job reaches, the second how many process records the holder can
/// even see. Users are counted, never named — the question is which group, and a list of three thousand people
/// answers nothing while carrying every one of their names out of the building.
/// </summary>
public sealed record RunRole(Guid RoleId, string Name, string? BusinessUnit, int RunDepthMask, int ProcessDepthMask,
    int Users, IReadOnlyList<string> Teams);

/// <summary>A team, kept because a workflow's owner may be one and the owner column only carries a name.</summary>
public sealed record OwnerTeam(Guid TeamId, string Name, int? TeamType);

/// <summary>
/// Who may run a process, as far as CRM records it. <see cref="Note"/> carries why a piece is missing when it is:
/// an empty list because nobody holds the privilege and an empty list because the query was refused are different
/// answers, and a reader must not have to guess which one they are looking at.
/// </summary>
public sealed record RunAuthority(IReadOnlyList<RunRole> Roles, IReadOnlyList<OwnerTeam> Teams, string? Note)
{
    public static RunAuthority Empty { get; } = new([], [], null);
}

/// <summary>
/// The security side of "who can run this", as the browser export reduced it.
///
/// <para>
/// <b>CRM has no per-workflow permission.</b> There is no grant saying "role X may run workflow Y". Running a
/// process by hand needs one estate-wide privilege, <c>prvExecuteWorkflowJob</c>, plus the right to read the
/// process record at all (<c>prvReadWorkflow</c>), and the rest is the workflow's own on-demand flag and the
/// identity it runs as. The export reads the roles holding those two and COUNTS their holders in the browser, so
/// no list of people ever travels in the file; this reads what it wrote.
/// </para>
///
/// <para>
/// <b>What it cannot see:</b> a workflow shared with one user or team. Sharing lives in <c>principalobjectaccess</c>,
/// which the Web API does not expose, so a share grants access this list will never show. Said on the sheet.
/// </para>
/// </summary>
public static class RunAuthorityReader
{
    public const string IndexFile = Runs.RunPaths.Raw + "/yetkiler.json";

    public static OwnerTeam? ParseTeam(JsonElement row)
    {
        return Json.OptionalGuid(row, "teamid") is Guid id
            ? new OwnerTeam(id, Json.OptionalString(row, "name") ?? "", Json.OptionalInt(row, "teamtype"))
            : null;
    }

    /// <summary>What the browser export sends: the same shape, already reduced in the browser.</summary>
    public static RunAuthority Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return RunAuthority.Empty;
        }
        return new RunAuthority(
            [.. Rows(root, "roles").Select(ParseRole).OfType<RunRole>()],
            [.. Rows(root, "teams").Select(ParseTeam).OfType<OwnerTeam>().OrderBy(team => team.TeamId)],
            Json.OptionalString(root, "note"));
    }

    private static RunRole? ParseRole(JsonElement row)
    {
        return Json.OptionalGuid(row, "roleId") is Guid id
            ? new RunRole(id, Json.OptionalString(row, "name") ?? "", Json.OptionalString(row, "businessUnit"),
                Json.OptionalInt(row, "runDepthMask") ?? 0, Json.OptionalInt(row, "processDepthMask") ?? 0,
                Json.OptionalInt(row, "users") ?? 0, Names(row, "teams"))
            : null;
    }

    private static IReadOnlyList<string> Names(JsonElement row, string property)
    {
        return row.TryGetProperty(property, out JsonElement values) && values.ValueKind == JsonValueKind.Array
            ? [.. values.EnumerateArray().Select(value => value.GetString()).OfType<string>()]
            : [];
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];
    }
}
