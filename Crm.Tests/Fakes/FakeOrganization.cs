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

    public int DistinctOwners { get; set; } = 3;

    public string PrivilegeDepth { get; set; } = "Global";

    public IReadOnlyList<string> MissingAttributes { get; set; } = [];

    /// <summary>Raw <c>category</c> value for record 0, to stage an option-set value outside the §3.1 table.</summary>
    public int FirstCategory { get; set; }

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
        return server;
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
            + $"\"iscrmuiworkflow\":true,\"versionnumber\":\"{1000 + index}\",\"_ownerid_value\":\"{owner:D}\","
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
