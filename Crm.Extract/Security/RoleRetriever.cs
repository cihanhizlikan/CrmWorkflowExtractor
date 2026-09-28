using System.Text.Json;
using Crm.Extract.Http;

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
/// Reads the security side of "who can run this" with GETs like everything else.
///
/// <para>
/// <b>CRM has no per-workflow permission.</b> There is no grant saying "role X may run workflow Y". Running a
/// process by hand needs one estate-wide privilege, <c>prvExecuteWorkflowJob</c>, plus the right to read the
/// process record at all (<c>prvReadWorkflow</c>), and the rest is the workflow's own on-demand flag and the
/// identity it runs as. So this reads the roles that hold those two privileges and who holds those roles; what
/// varies per workflow is read from the workflow record itself, not from here.
/// </para>
///
/// <para>
/// <b>What it cannot see:</b> a workflow shared with one user or team. Sharing lives in <c>principalobjectaccess</c>,
/// which the Web API does not expose, so a share grants access this list will never show. Said on the sheet.
/// </para>
/// </summary>
public sealed class RoleRetriever(CrmHttpClient client, int pageSize)
{
    public const string IndexFile = Runs.RunPaths.Raw + "/yetkiler.json";

    public const string RunPrivilege = "prvExecuteWorkflowJob";

    public const string ReadPrivilege = "prvReadWorkflow";

    /// <summary>
    /// The three intersect tables this needs. Their entity SET names are not their logical names and differ by
    /// version, so they are asked for rather than assumed — the same reason <see cref="Preflight.WorkflowColumns"/>
    /// checks its columns against metadata instead of trusting a documented list.
    /// </summary>
    private static readonly string[] Intersects = ["roleprivileges", "systemuserroles", "teamroles"];

    public async Task<RunAuthority> RetrieveAsync(CancellationToken token)
    {
        try
        {
            return await ReadAsync(token);
        }
        catch (Exception error) when (error is CrmRequestException or JsonException or InvalidOperationException)
        {
            return RunAuthority.Empty with { Note = error.Message };
        }
    }

    private async Task<RunAuthority> ReadAsync(CancellationToken token)
    {
        IReadOnlyDictionary<string, string> sets = await EntitySetsAsync(token);
        string filter = $"name eq '{RunPrivilege}' or name eq '{ReadPrivilege}'";
        CrmResponse response = await client.GetAsync($"privileges?$select=privilegeid,name&$filter={Uri.EscapeDataString(filter)}", CrmPreferences.None, token);
        (Guid? run, Guid? read) = Privileges(response.Body);
        if (run is not Guid runPrivilege)
        {
            return RunAuthority.Empty with { Note = $"'{RunPrivilege}' bu kurulumda yok; elle çalıştırma yetkisi okunamadı." };
        }

        List<JsonElement> grants = await PageAsync($"{sets["roleprivileges"]}?$filter={Uri.EscapeDataString(PrivilegeFilter(run, read))}", token);
        IReadOnlySet<Guid> runRoles = RunRoles(grants, runPrivilege);
        if (runRoles.Count == 0)
        {
            return RunAuthority.Empty with { Teams = await TeamsAsync(token), Note = $"Hiçbir güvenlik rolü '{RunPrivilege}' taşımıyor." };
        }

        List<JsonElement> roles = await PageAsync("roles?$select=roleid,name,_businessunitid_value", token);
        List<JsonElement> userRoles = await PageAsync($"{sets["systemuserroles"]}?$filter={Uri.EscapeDataString(RoleFilter(runRoles))}", token);
        List<JsonElement> teamRoles = await PageAsync($"{sets["teamroles"]}?$filter={Uri.EscapeDataString(RoleFilter(runRoles))}", token);
        IReadOnlyList<OwnerTeam> teams = await TeamsAsync(token);
        return new RunAuthority(Build(grants, roles, userRoles, teamRoles, teams, runPrivilege, read), teams, null);
    }

    /// <summary>Logical name → entity set name, for the intersect tables whose set name cannot be guessed.</summary>
    /// <summary>
    /// <c>EntityDefinitions</c> is asked for EVERY entity's set name and the three are picked here, rather than
    /// filtered on the server. The metadata endpoint's <c>$filter</c> support is a narrow subset that differs by
    /// version, and a filter it will not honour does not return the wrong rows — it returns none, which reads
    /// exactly like "this server has no such table" and silently emptied the whole page. The unfiltered list is
    /// two short columns for a few hundred entities: one request, and nothing to be wrong about.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> EntitySetsAsync(CancellationToken token)
    {
        CrmResponse response = await client.GetAsync(
            "EntityDefinitions?$select=LogicalName,EntitySetName", CrmPreferences.None, token);
        Dictionary<string, string> sets = new(StringComparer.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(response.Body);
        foreach (JsonElement row in document.RootElement.GetProperty("value").EnumerateArray())
        {
            string? name = Json.OptionalString(row, "LogicalName");
            string? set = Json.OptionalString(row, "EntitySetName");
            if (name is not null && set is not null)
            {
                sets[name] = set;
            }
        }
        string[] absent = [.. Intersects.Where(name => !sets.ContainsKey(name))];
        if (absent.Length > 0)
        {
            throw new InvalidOperationException($"Bu sunucu şu tabloları bildirmiyor: {string.Join(", ", absent)}.");
        }
        return sets;
    }

    private async Task<IReadOnlyList<OwnerTeam>> TeamsAsync(CancellationToken token)
    {
        List<JsonElement> rows = await PageAsync("teams?$select=teamid,name,teamtype", token);
        return [.. rows.Select(ParseTeam).OfType<OwnerTeam>().OrderBy(team => team.TeamId)];
    }

    private async Task<List<JsonElement>> PageAsync(string path, CancellationToken token)
    {
        ODataPager pager = new(client);
        List<JsonElement> records = [];
        await foreach (ODataPage page in pager.GetPagesAsync(path, pageSize, token))
        {
            records.AddRange(page.Records);
        }
        return records;
    }

    private static string PrivilegeFilter(Guid? run, Guid? read)
    {
        List<string> parts = [.. new[] { run, read }.OfType<Guid>().Select(id => $"privilegeid eq {id:D}")];
        return string.Join(" or ", parts);
    }

    private static string RoleFilter(IEnumerable<Guid> roles)
    {
        return string.Join(" or ", roles.Select(id => $"roleid eq {id:D}"));
    }

    public static (Guid? Run, Guid? Read) Privileges(string body)
    {
        Guid? run = null;
        Guid? read = null;
        using JsonDocument document = JsonDocument.Parse(body);
        foreach (JsonElement row in document.RootElement.GetProperty("value").EnumerateArray())
        {
            string? name = Json.OptionalString(row, "name");
            Guid? id = Json.OptionalGuid(row, "privilegeid");
            if (string.Equals(name, RunPrivilege, StringComparison.OrdinalIgnoreCase))
            {
                run = id;
            }
            if (string.Equals(name, ReadPrivilege, StringComparison.OrdinalIgnoreCase))
            {
                read = id;
            }
        }
        return (run, read);
    }

    /// <summary>
    /// An intersect table's key columns are plain identifiers (<c>roleid</c>), but the Web API writes a lookup as
    /// <c>_roleid_value</c> and which form a given table uses is a version detail. Both are accepted.
    /// </summary>
    public static Guid? Lookup(JsonElement row, string name)
    {
        return Json.OptionalGuid(row, name) ?? Json.OptionalGuid(row, "_" + name + "_value");
    }

    public static OwnerTeam? ParseTeam(JsonElement row)
    {
        return Json.OptionalGuid(row, "teamid") is Guid id
            ? new OwnerTeam(id, Json.OptionalString(row, "name") ?? "", Json.OptionalInt(row, "teamtype"))
            : null;
    }

    /// <summary>
    /// The roles that may START a process, and only those. Holding <c>prvReadWorkflow</c> lets a role SEE processes
    /// and is read here to report its depth; a list that let a read grant through would name people as able to run
    /// things they cannot, which is the one way this page could do harm. One function decides it, and it is tested.
    /// </summary>
    public static IReadOnlySet<Guid> RunRoles(IReadOnlyList<JsonElement> grants, Guid runPrivilege)
    {
        return grants.Where(grant => Lookup(grant, "privilegeid") == runPrivilege)
            .Select(grant => Lookup(grant, "roleid")).OfType<Guid>().ToHashSet();
    }

    public static IReadOnlyList<RunRole> Build(IReadOnlyList<JsonElement> grants,
        IReadOnlyList<JsonElement> roles, IReadOnlyList<JsonElement> userRoles, IReadOnlyList<JsonElement> teamRoles,
        IReadOnlyList<OwnerTeam> teams, Guid runPrivilege, Guid? readPrivilege)
    {
        IReadOnlySet<Guid> runRoles = RunRoles(grants, runPrivilege);
        Dictionary<Guid, (string Name, string? Unit)> named = [];
        foreach (JsonElement role in roles)
        {
            if (Json.OptionalGuid(role, "roleid") is Guid id)
            {
                named[id] = (Json.OptionalString(role, "name") ?? "", Json.FormattedValue(role, "_businessunitid_value"));
            }
        }
        Dictionary<Guid, string> teamNames = [];
        foreach (OwnerTeam team in teams)
        {
            teamNames[team.TeamId] = team.Name;
        }

        Dictionary<Guid, int> runDepth = [];
        Dictionary<Guid, int> readDepth = [];
        foreach (JsonElement grant in grants)
        {
            Guid? role = Lookup(grant, "roleid");
            Guid? privilege = Lookup(grant, "privilegeid");
            int mask = Json.OptionalInt(grant, "privilegedepthmask") ?? 0;
            if (role is not Guid roleId)
            {
                continue;
            }
            Dictionary<Guid, int> target = privilege == runPrivilege ? runDepth : readDepth;
            if (privilege == runPrivilege || (readPrivilege is Guid read && privilege == read))
            {
                target[roleId] = Math.Max(target.GetValueOrDefault(roleId), mask);
            }
        }

        Dictionary<Guid, int> users = [];
        foreach (JsonElement row in userRoles)
        {
            if (Lookup(row, "roleid") is Guid roleId)
            {
                users[roleId] = users.GetValueOrDefault(roleId) + 1;
            }
        }
        Dictionary<Guid, List<string>> byRole = [];
        foreach (JsonElement row in teamRoles)
        {
            if (Lookup(row, "roleid") is Guid roleId && Lookup(row, "teamid") is Guid teamId)
            {
                (byRole.TryGetValue(roleId, out List<string>? list) ? list : byRole[roleId] = [])
                    .Add(teamNames.GetValueOrDefault(teamId, teamId.ToString("D")));
            }
        }

        return
        [
            .. runRoles
                .Select(id => new RunRole(id, named.GetValueOrDefault(id).Name ?? "", named.GetValueOrDefault(id).Unit,
                    runDepth.GetValueOrDefault(id), readDepth.GetValueOrDefault(id), users.GetValueOrDefault(id),
                    [.. (byRole.GetValueOrDefault(id) ?? []).Order(StringComparer.Ordinal)]))
                .OrderByDescending(role => role.Users)
                .ThenBy(role => role.Name, StringComparer.Ordinal)
        ];
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
