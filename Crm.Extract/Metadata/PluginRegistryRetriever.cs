using System.Text.Json;
using Crm.Extract.Http;

namespace Crm.Extract.Metadata;

/// <summary>
/// An assembly registered in CRM: the unit a partner or an in-house team ships and owns. <see cref="Addresses"/>
/// are the endpoints found in its own bytes — what the code reaches out to, which no workflow record carries.
/// </summary>
public sealed record PluginAssembly(Guid AssemblyId, string Name, string? Version, int? SourceType, bool? IsManaged, IReadOnlyList<string> Addresses);

/// <summary>One type inside a registered assembly. A workflow's custom activity is one of these.</summary>
public sealed record PluginType(Guid TypeId, Guid? AssemblyId, string TypeName, string? FriendlyName, bool? IsWorkflowActivity, string? ActivityGroup);

/// <summary>
/// A registered plug-in step: code CRM runs on a message, outside any workflow. Its <c>configuration</c> is the
/// unsecure registration text, which is where an endpoint is classically put.
/// </summary>
public sealed record PluginStep(Guid StepId, Guid? TypeId, string Name, string? Configuration, int? Stage, int? Mode, int? State);

/// <summary>What CRM knows about the code registered in it.</summary>
public sealed record PluginRegistry(IReadOnlyList<PluginAssembly> Assemblies, IReadOnlyList<PluginType> Types, IReadOnlyList<PluginStep> Steps)
{
    public static PluginRegistry Empty { get; } = new([], [], []);
}

/// <summary>
/// The registered code, read with GETs like everything else (§3.5). Two questions it answers that the workflow
/// definitions cannot: whether a custom activity a workflow calls is still registered at all, and what else in
/// CRM reaches outside — plug-in steps are not workflows and never appear in the process inventory.
///
/// <para>
/// The <b>secure</b> configuration is deliberately not read. It is a separate entity, it is where credentials are
/// kept, and this tool is looking for addresses.
/// </para>
/// </summary>
public sealed class PluginRegistryRetriever(CrmHttpClient client, int pageSize)
{
    public const string IndexFile = Runs.RunPaths.Raw + "/eklentiler.json";

    public async Task<PluginRegistry> RetrieveAsync(CancellationToken token)
    {
        List<PluginAssembly> assemblies = [.. (await PageAsync("pluginassemblies?$select=pluginassemblyid,name,version,sourcetype,ismanaged", token))
            .Select(ParseAssembly).OfType<PluginAssembly>().OrderBy(assembly => assembly.AssemblyId)];
        List<PluginType> types = [.. (await PageAsync("plugintypes?$select=plugintypeid,typename,friendlyname,isworkflowactivity,workflowactivitygroupname,_pluginassemblyid_value", token))
            .Select(ParseType).OfType<PluginType>().OrderBy(type => type.TypeId)];
        List<PluginStep> steps = [.. (await PageAsync("sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid,name,configuration,stage,mode,statecode,_plugintypeid_value", token))
            .Select(ParseStep).OfType<PluginStep>().OrderBy(step => step.StepId)];
        return new PluginRegistry([.. await WithAddressesAsync(assemblies, types, token)], types, steps);
    }

    /// <summary>
    /// The endpoints in the assemblies that back a workflow's custom activities — the only assemblies worth
    /// downloading, and the only place the address of a service call is written down. The bytes are read, scanned
    /// and dropped: nothing but the addresses is kept, because a production DLL on disk is a liability.
    /// </summary>
    private async Task<List<PluginAssembly>> WithAddressesAsync(List<PluginAssembly> assemblies, List<PluginType> types, CancellationToken token)
    {
        HashSet<Guid> wanted = [.. types.Where(type => type.IsWorkflowActivity == true && type.AssemblyId is not null).Select(type => type.AssemblyId!.Value)];
        List<PluginAssembly> read = [];
        foreach (PluginAssembly assembly in assemblies)
        {
            if (!wanted.Contains(assembly.AssemblyId))
            {
                read.Add(assembly);
                continue;
            }
            read.Add(assembly with { Addresses = await AddressesAsync(assembly.AssemblyId, token) });
        }
        return read;
    }

    private async Task<IReadOnlyList<string>> AddressesAsync(Guid assemblyId, CancellationToken token)
    {
        try
        {
            CrmResponse response = await client.GetAsync($"pluginassemblies({assemblyId:D})?$select=content", CrmPreferences.None, token);
            using JsonDocument document = JsonDocument.Parse(response.Body);
            string? content = Json.OptionalString(document.RootElement, "content");
            return content is null ? [] : AssemblyStrings.Addresses(Convert.FromBase64String(content));
        }
        catch (Exception error) when (error is CrmRequestException or FormatException or JsonException)
        {
            // A refused or unreadable assembly leaves that activity's addresses unknown, which the report says.
            return [];
        }
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

    public static PluginAssembly? ParseAssembly(JsonElement row)
    {
        return Json.OptionalGuid(row, "pluginassemblyid") is Guid id
            ? new PluginAssembly(id, Json.OptionalString(row, "name") ?? "", Json.OptionalString(row, "version"),
                Json.OptionalInt(row, "sourcetype"), Json.OptionalBool(row, "ismanaged"), Addresses(row))
            : null;
    }

    /// <summary>
    /// The addresses an export already extracted. The browser export scans the assembly in the browser and sends
    /// only the text, so a multi-megabyte DLL never lands in the export file or on this machine.
    /// </summary>
    private static IReadOnlyList<string> Addresses(JsonElement row)
    {
        return row.TryGetProperty("addresses", out JsonElement addresses) && addresses.ValueKind == JsonValueKind.Array
            ? [.. addresses.EnumerateArray().Select(address => address.GetString()).OfType<string>()]
            : [];
    }

    public static PluginType? ParseType(JsonElement row)
    {
        return Json.OptionalGuid(row, "plugintypeid") is Guid id
            ? new PluginType(id, Json.OptionalGuid(row, "_pluginassemblyid_value"), Json.OptionalString(row, "typename") ?? "",
                Json.OptionalString(row, "friendlyname"), Json.OptionalBool(row, "isworkflowactivity"),
                Json.OptionalString(row, "workflowactivitygroupname"))
            : null;
    }

    public static PluginStep? ParseStep(JsonElement row)
    {
        return Json.OptionalGuid(row, "sdkmessageprocessingstepid") is Guid id
            ? new PluginStep(id, Json.OptionalGuid(row, "_plugintypeid_value"), Json.OptionalString(row, "name") ?? "",
                Json.OptionalString(row, "configuration"), Json.OptionalInt(row, "stage"), Json.OptionalInt(row, "mode"),
                Json.OptionalInt(row, "statecode"))
            : null;
    }

    public static PluginRegistry Parse(JsonElement root)
    {
        return new PluginRegistry(
            [.. Rows(root, "assemblies").Select(ParseAssembly).OfType<PluginAssembly>().OrderBy(assembly => assembly.AssemblyId)],
            [.. Rows(root, "types").Select(ParseType).OfType<PluginType>().OrderBy(type => type.TypeId)],
            [.. Rows(root, "steps").Select(ParseStep).OfType<PluginStep>().OrderBy(step => step.StepId)]);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];
    }
}
