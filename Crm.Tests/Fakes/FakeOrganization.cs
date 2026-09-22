using System.Globalization;
using System.Text;
using Crm.Extract.Http;
using Crm.Extract.Preflight;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crm.Tests.Fakes;

/// <summary>A synthetic organization: a user, privileges, workflow metadata and N workflows served in pages.</summary>
internal sealed class FakeOrganization
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public int WorkflowCount { get; set; } = 45;

    /// <summary>What <c>workflows/$count</c> answers; defaults to the true count.</summary>
    public int? ReportedCount { get; set; }

    /// <summary>What the FetchXML aggregate count answers; null makes the server refuse it with 400.</summary>
    public int? AggregateCount { get; set; }

    public int DistinctOwners { get; set; } = 3;

    public string PrivilegeDepth { get; set; } = "Global";

    public IReadOnlyList<string> MissingAttributes { get; set; } = [];

    /// <summary>Raw <c>category</c> value for record 0, to stage an option-set value outside the §3.1 table.</summary>
    public int FirstCategory { get; set; }

    /// <summary>Added to every versionnumber, to simulate workflows edited between two runs.</summary>
    public int VersionOffset { get; set; }

    /// <summary>Record indexes whose <c>iscrmuiworkflow</c> is false (hand-authored XAML).</summary>
    public IReadOnlySet<int> NonDesigner { get; set; } = new HashSet<int>();

    /// <summary>Definition indexes whose activation runs different logic from the definition.</summary>
    public IReadOnlySet<int> Drifted { get; set; } = new HashSet<int>();

    /// <summary>Record indexes whose XAML request fails with 404.</summary>
    public IReadOnlySet<int> XamlMissing { get; set; } = new HashSet<int>();

    /// <summary>The XAML served for a record index; the definition index and whether it is the activation copy are passed.</summary>
    public Func<int, bool, string> XamlFor { get; set; } = DefaultXaml;

    public FakeCrmServer Build()
    {
        FakeCrmServer server = new();
        server.OnJson("WhoAmI()", $"{{\"UserId\":\"{UserId:D}\",\"BusinessUnitId\":\"22222222-2222-2222-2222-222222222222\",\"OrganizationId\":\"33333333-3333-3333-3333-333333333333\"}}");
        server.OnJson($"systemusers({UserId:D})?", "{\"fullname\":\"Servis Hesabı\",\"domainname\":\"CORP\\\\svc-crm-read\"}");
        server.OnJson("privileges?", PrivilegesBody());
        server.OnJson($"systemusers({UserId:D})/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()", RolePrivilegesBody(PrivilegeDepth));
        server.OnJson("EntityDefinitions(LogicalName='workflow')/Attributes", AttributesBody());
        server.On("workflows/$count", _ => FakeCrmServer.Text((ReportedCount ?? WorkflowCount).ToString(CultureInfo.InvariantCulture)));
        server.On("workflows?", Page);
        server.On("workflows?fetchXml=", _ => AggregateCount is int aggregate
            ? FakeCrmServer.Json("{\"value\":[{\"n\":" + aggregate.ToString(CultureInfo.InvariantCulture) + "}]}")
            : FakeCrmServer.Json("{\"error\":{\"message\":\"Aggregate query refused\"}}", System.Net.HttpStatusCode.BadRequest));
        server.On("workflows(", Xaml);
        server.OnJson("EntityDefinitions(LogicalName='new_policy')/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
            "{\"value\":[{\"LogicalName\":\"new_status\",\"OptionSet\":{\"Options\":[{\"Value\":100000003,\"Label\":{\"UserLocalizedLabel\":{\"Label\":\"İptal Edildi\"}}},{\"Value\":100000007,\"Label\":{\"UserLocalizedLabel\":{\"Label\":\"Askıda\"}}}]}}]}");
        server.OnJson("EntityDefinitions(LogicalName='new_policy')/Attributes/Microsoft.Dynamics.CRM.StatusAttributeMetadata", "{\"value\":[]}");
        server.OnJson("EntityDefinitions(LogicalName='new_policy')/Attributes/Microsoft.Dynamics.CRM.StateAttributeMetadata", "{\"value\":[]}");
        server.OnJson("processstages?", "{\"value\":[]}");
        return server;
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

    private HttpResponseMessage Xaml(Uri uri)
    {
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        int open = path.LastIndexOf('(');
        Guid id = Guid.Parse(path[(open + 1)..path.LastIndexOf(')')]);
        int index = int.Parse(id.ToString("D")[^12..], CultureInfo.InvariantCulture);
        if (XamlMissing.Contains(index))
        {
            return FakeCrmServer.Json("{\"error\":{\"message\":\"Not found\"}}", System.Net.HttpStatusCode.NotFound);
        }
        bool activation = index % 2 == 1;
        int definitionIndex = activation ? index - 1 : index;
        string xaml = XamlFor(index, activation && Drifted.Contains(definitionIndex));
        string escaped = System.Text.Json.JsonSerializer.Serialize(xaml);
        return FakeCrmServer.Json($"{{\"workflowid\":\"{id:D}\",\"xaml\":{escaped}}}");
    }

    public static CrmConnectionOptions Options()
    {
        return new CrmConnectionOptions { WebApiBaseUrl = FakeCrmServer.BaseUrl, PageSize = 20, MaxAttempts = 3 };
    }

    public static RetryPolicy NoWaitRetry(List<TimeSpan>? delays = null)
    {
        return new RetryPolicy(3, TimeSpan.FromSeconds(1), (wait, _) =>
        {
            delays?.Add(wait);
            return Task.CompletedTask;
        }, () => 0, NullLogger.Instance);
    }

    public static CrmHttpClient Client(FakeCrmServer server, List<TimeSpan>? delays = null)
    {
        return new CrmHttpClient(Options(), server, NoWaitRetry(delays), NullLogger.Instance);
    }

    public static CrmHttpClient ClientFor(FakeCrmServer server, CrmConnectionOptions options, ILogger logger)
    {
        return new CrmHttpClient(options, server, NoWaitRetry(), logger);
    }

    public static Guid WorkflowId(int index)
    {
        return Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-{index:D12}"));
    }

    private HttpResponseMessage Page(Uri uri)
    {
        const int pageSize = 20;
        string query = Uri.UnescapeDataString(uri.Query);
        int page = 1;
        int marker = query.IndexOf("$skiptoken=page", StringComparison.Ordinal);
        if (marker >= 0)
        {
            page = int.Parse(query[(marker + "$skiptoken=page".Length)..], CultureInfo.InvariantCulture);
        }
        int start = (page - 1) * pageSize;
        int end = Math.Min(WorkflowCount, start + pageSize);

        StringBuilder body = new("{\"@odata.context\":\"" + FakeCrmServer.BaseUrl + "$metadata#workflows\",\"value\":[");
        for (int index = start; index < end; index++)
        {
            if (index > start)
            {
                body.Append(',');
            }
            body.Append(Record(index));
        }
        body.Append(']');
        if (end < WorkflowCount)
        {
            string firstQuery = query.Contains("&$skiptoken", StringComparison.Ordinal) ? query[..query.IndexOf("&$skiptoken", StringComparison.Ordinal)] : query;
            body.Append(CultureInfo.InvariantCulture, $",\"@odata.nextLink\":\"{FakeCrmServer.BaseUrl}workflows{firstQuery}&$skiptoken=page{page + 1}\"");
        }
        body.Append('}');
        return FakeCrmServer.Json(body.ToString());
    }

    /// <summary>Even indexes are definitions, odd are the activation of the definition before them.</summary>
    private string Record(int index)
    {
        bool definition = index % 2 == 0;
        int category = index == 0 ? FirstCategory : 0;
        Guid owner = Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"99999999-0000-0000-0000-{index % Math.Max(1, DistinctOwners):D12}"));
        string parent = definition ? "null" : $"\"{WorkflowId(index - 1):D}\"";
        string active = definition && index + 1 < WorkflowCount ? $"\"{WorkflowId(index + 1):D}\"" : "null";
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"workflowid\":\"{WorkflowId(index):D}\",\"name\":\"Poliçe İptal Süreci {index}\",\"primaryentity\":\"new_policy\","
            + $"\"category\":{category},\"category@OData.Community.Display.V1.FormattedValue\":\"İş Akışı\","
            + $"\"type\":{(definition ? 1 : 2)},\"mode\":0,\"scope\":4,\"statecode\":1,\"runas\":1,"
            + $"\"iscrmuiworkflow\":{(NonDesigner.Contains(index) ? "false" : "true")},\"versionnumber\":\"{1000 + index + VersionOffset}\",\"_ownerid_value\":\"{owner:D}\","
            + $"\"_parentworkflowid_value\":{parent},\"_activeworkflowid_value\":{active}}}");
    }

    private static string PrivilegesBody()
    {
        IEnumerable<string> rows = PrivilegeCheck.Required.Select((privilege, index) =>
            $"{{\"privilegeid\":\"{PrivilegeId(index):D}\",\"name\":\"{privilege.Name}\"}}");
        return "{\"value\":[" + string.Join(",", rows) + "]}";
    }

    private static string RolePrivilegesBody(string depth)
    {
        IEnumerable<string> rows = PrivilegeCheck.Required.Select((_, index) =>
            $"{{\"Depth\":\"{depth}\",\"PrivilegeId\":\"{PrivilegeId(index):D}\",\"BusinessUnitId\":\"22222222-2222-2222-2222-222222222222\"}}");
        return "{\"RolePrivileges\":[" + string.Join(",", rows) + "]}";
    }

    private string AttributesBody()
    {
        IEnumerable<string> names = WorkflowColumns.Inventory
            .Select(WorkflowColumns.AttributeNameOf)
            .Append("xaml")
            .Where(name => !MissingAttributes.Contains(name, StringComparer.Ordinal))
            .Select(name => $"{{\"LogicalName\":\"{name}\"}}");
        return "{\"value\":[" + string.Join(",", names) + "]}";
    }

    private static Guid PrivilegeId(int index)
    {
        return Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"aaaaaaaa-0000-0000-0000-{index:D12}"));
    }
}
