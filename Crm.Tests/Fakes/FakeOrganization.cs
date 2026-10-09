using System.Globalization;
using System.Text.Json.Nodes;
using Crm.Extract.Preflight;

namespace Crm.Tests.Fakes;

/// <summary>
/// A synthetic organization: a user, privileges, workflow metadata and N workflows — as the browser export would
/// describe it.
///
/// <para>
/// It used to serve the same organization over a fake HTTP server, because the tool fetched from CRM itself. The
/// tool no longer has a network path: every run reads a file <c>tools/crm-browser-export.js</c> wrote, so the
/// organization is now written as that file, section for section — the shapes are the ones the real script produced
/// in <c>Fixtures/BrowserExport/mock-crm-export.json</c>. The scenario knobs mean what they always meant.
/// </para>
/// </summary>
internal sealed class FakeOrganization
{
    public const string BaseUrl = "https://crm.example.local/org/api/data/v8.2/";

    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Guid AssemblyId { get; } = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    public static Guid PluginTypeId { get; } = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    /// <summary>The address the assembly carries, and the only place in this organization it is written down.</summary>
    public const string AssemblyAddress = "https://nova.ornek.local/imza/v2";

    public int WorkflowCount { get; set; } = 45;

    /// <summary>The count the export reports; defaults to the true count.</summary>
    public int? ReportedCount { get; set; }

    public int DistinctOwners { get; set; } = 3;

    public string PrivilegeDepth { get; set; } = "Global";

    public IReadOnlyList<string> MissingAttributes { get; set; } = [];

    /// <summary>Raw <c>category</c> value for record 0, to stage an option-set value outside the §3.1 table.</summary>
    public int FirstCategory { get; set; }

    /// <summary>Record indexes whose <c>iscrmuiworkflow</c> is false (hand-authored XAML).</summary>
    public IReadOnlySet<int> NonDesigner { get; set; } = new HashSet<int>();

    /// <summary>Record indexes CRM reports as part of a managed solution: supplied with the product.</summary>
    public IReadOnlySet<int> Managed { get; set; } = new HashSet<int>();

    /// <summary>Definition indexes whose activation runs different logic from the definition.</summary>
    public IReadOnlySet<int> Drifted { get; set; } = new HashSet<int>();

    /// <summary>Record indexes whose XAML the export failed to read.</summary>
    public IReadOnlySet<int> XamlMissing { get; set; } = new HashSet<int>();

    /// <summary>The XAML for a record index; the definition index and whether it is a drifted activation copy are passed.</summary>
    public Func<int, bool, string> XamlFor { get; set; } = DefaultXaml;

    public static Guid WorkflowId(int index)
    {
        return Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-{index:D12}"));
    }

    /// <summary>
    /// A minimal designer-shaped workflow. Its class name carries the record id, as real XAML does, so a definition
    /// and its activation differ in bytes but not in structure unless the definition index is in <see cref="Drifted"/>.
    /// </summary>
    public static string DefaultXaml(int recordIndex, bool drifted)
    {
        string className = "XrmWorkflow" + WorkflowId(recordIndex).ToString("N");
        string status = drifted ? "100000007" : "100000003";
        return $"<Activity x:Class=\"{className}\" xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\" "
            + "xmlns:mxswa=\"clr-namespace:Microsoft.Xrm.Sdk.Workflow.Activities;assembly=Microsoft.Xrm.Sdk.Workflow, Version=8.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\" "
            + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><mxswa:Workflow>"
            + $"<Sequence DisplayName=\"UpdateStep1: Durum\"><mxswa:UpdateEntity DisplayName=\"UpdateStep1\" EntityName=\"new_policy\" Status=\"{status}\" /></Sequence>"
            + "</mxswa:Workflow></Activity>";
    }

    /// <summary>The organization as one <c>crm-browser-export/1</c> document.</summary>
    public JsonObject Export()
    {
        JsonArray workflows = [];
        JsonObject xaml = [];
        JsonObject xamlErrors = [];
        for (int index = 0; index < WorkflowCount; index++)
        {
            workflows.Add(Record(index));
            string id = WorkflowId(index).ToString("D");
            if (XamlMissing.Contains(index))
            {
                xamlErrors[id] = $"GET workflows({id})?$select=xaml -> 404: {{\"error\":{{\"message\":\"Not found\"}}}}";
                continue;
            }
            bool activation = index % 2 == 1;
            int definitionIndex = activation ? index - 1 : index;
            xaml[id] = XamlFor(index, activation && Drifted.Contains(definitionIndex));
        }

        List<string> attributes = [.. WorkflowColumns.Inventory.Select(WorkflowColumns.AttributeNameOf).Append("xaml")
            .Where(name => !MissingAttributes.Contains(name, StringComparer.Ordinal))];
        return new JsonObject
        {
            ["format"] = "crm-browser-export/1",
            ["exportedAtUtc"] = "2026-09-22T12:00:00.000Z",
            ["startedAtUtc"] = "2026-09-22T11:58:00.000Z",
            ["webApiRoot"] = BaseUrl,
            ["whoAmI"] = new JsonObject
            {
                ["UserId"] = UserId.ToString("D"),
                ["BusinessUnitId"] = "22222222-2222-2222-2222-222222222222",
                ["OrganizationId"] = "33333333-3333-3333-3333-333333333333"
            },
            ["user"] = new JsonObject { ["fullname"] = "Servis Hesabı", ["domainname"] = "CORP\\svc-crm-read" },
            ["privileges"] = new JsonObject
            {
                ["value"] = new JsonArray([.. PrivilegeCheck.Required.Select((privilege, index) => new JsonObject
                {
                    ["privilegeid"] = PrivilegeId(index).ToString("D"),
                    ["name"] = privilege.Name
                })])
            },
            ["userPrivileges"] = new JsonObject
            {
                ["RolePrivileges"] = new JsonArray([.. PrivilegeCheck.Required.Select((_, index) => new JsonObject
                {
                    ["Depth"] = PrivilegeDepth,
                    ["PrivilegeId"] = PrivilegeId(index).ToString("D"),
                    ["BusinessUnitId"] = "22222222-2222-2222-2222-222222222222"
                })])
            },
            ["workflowAttributes"] = new JsonObject
            {
                ["value"] = new JsonArray([.. attributes.Select(name => new JsonObject { ["LogicalName"] = name })])
            },
            ["count"] = ReportedCount ?? WorkflowCount,
            ["columns"] = new JsonArray([.. attributes.Where(name => name != "xaml").Select(name => JsonValue.Create(name))]),
            ["workflows"] = workflows,
            ["xaml"] = xaml,
            ["xamlErrors"] = xamlErrors,
            ["optionSets"] = new JsonObject
            {
                ["new_policy"] = new JsonObject
                {
                    ["PicklistAttributeMetadata"] = JsonNode.Parse(
                        "{\"value\":[{\"LogicalName\":\"new_status\",\"OptionSet\":{\"Options\":[{\"Value\":100000003,\"Label\":{\"UserLocalizedLabel\":{\"Label\":\"İptal Edildi\"}}},{\"Value\":100000007,\"Label\":{\"UserLocalizedLabel\":{\"Label\":\"Askıda\"}}}]}}]}"),
                    ["StatusAttributeMetadata"] = JsonNode.Parse("{\"value\":[]}"),
                    ["StateAttributeMetadata"] = JsonNode.Parse("{\"value\":[]}")
                }
            },
            ["processStages"] = new JsonArray(),
            ["processTriggers"] = new JsonArray(),
            ["caseStages"] = new JsonArray(),
            ["plugins"] = Plugins(),
            ["runAuthority"] = JsonNode.Parse("{\"roles\":[],\"teams\":[],\"note\":\"Bu organizasyonda rol kaydı yok.\"}")
        };
    }

    /// <summary>The export written to a file in <paramref name="directory"/>, as a run would be given it.</summary>
    public string WriteExport(string directory)
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"crm-export-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, Export().ToJsonString());
        return file;
    }

    /// <summary>Even indexes are definitions, odd are the activation of the definition before them.</summary>
    private JsonObject Record(int index)
    {
        bool definition = index % 2 == 0;
        Guid owner = Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"99999999-0000-0000-0000-{index % Math.Max(1, DistinctOwners):D12}"));
        return new JsonObject
        {
            ["workflowid"] = WorkflowId(index).ToString("D"),
            ["name"] = string.Create(CultureInfo.InvariantCulture, $"Poliçe İptal Süreci {index}"),
            ["primaryentity"] = "new_policy",
            ["category"] = index == 0 ? FirstCategory : 0,
            ["category@OData.Community.Display.V1.FormattedValue"] = "İş Akışı",
            ["type"] = definition ? 1 : 2,
            ["mode"] = 0,
            ["scope"] = 4,
            ["statecode"] = 1,
            ["runas"] = 1,
            ["iscrmuiworkflow"] = !NonDesigner.Contains(index),
            ["ismanaged"] = Managed.Contains(index),
            ["versionnumber"] = (1000 + index).ToString(CultureInfo.InvariantCulture),
            ["_ownerid_value"] = owner.ToString("D"),
            ["_parentworkflowid_value"] = definition ? null : WorkflowId(index - 1).ToString("D"),
            ["_activeworkflowid_value"] = definition && index + 1 < WorkflowCount ? WorkflowId(index + 1).ToString("D") : null
        };
    }

    /// <summary>
    /// The plug-in registry as the export sends it: the browser has already scanned the assembly, so only the
    /// addresses travel, never the bytes.
    /// </summary>
    private static JsonObject Plugins()
    {
        return new JsonObject
        {
            ["assemblies"] = new JsonArray(new JsonObject
            {
                ["pluginassemblyid"] = AssemblyId.ToString("D"),
                ["name"] = "Partner.Crm.Activities",
                ["version"] = "2.1.0.0",
                ["sourcetype"] = 0,
                ["ismanaged"] = false,
                ["addresses"] = new JsonArray(AssemblyAddress)
            }),
            ["types"] = new JsonArray(new JsonObject
            {
                ["plugintypeid"] = PluginTypeId.ToString("D"),
                ["typename"] = "Partner.Crm.Activities.NotifyPolicyService",
                ["friendlyname"] = "Poliçe servisi",
                ["isworkflowactivity"] = true,
                ["workflowactivitygroupname"] = "Partner",
                ["_pluginassemblyid_value"] = AssemblyId.ToString("D")
            }),
            ["steps"] = new JsonArray(new JsonObject
            {
                ["sdkmessageprocessingstepid"] = "aaaaaaaa-0000-0000-0000-000000000003",
                ["name"] = "Partner.Crm.Plugins.CaseRouter: Create of incident",
                ["configuration"] = "<ayarlar><servis>https://kuyruk.ornek.local/route</servis></ayarlar>",
                ["stage"] = 40,
                ["mode"] = 0,
                ["statecode"] = 0,
                ["_plugintypeid_value"] = PluginTypeId.ToString("D")
            })
        };
    }

    private static Guid PrivilegeId(int index)
    {
        return Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"aaaaaaaa-0000-0000-0000-{index:D12}"));
    }
}
