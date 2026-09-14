using System.Net;
using Crm.Extract.Http;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;
using Crm.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crm.Tests.Extract;

public sealed class PagingAndRetryTests
{
    [Fact]
    public async Task Paging_Follows_Next_Links_To_Exhaustion_With_A_Modest_Page_Size()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 45 }.Build();
        using CrmHttpClient client = FakeOrganization.Client(server);
        WorkflowInventoryRetriever retriever = new(client, 20, NullLogger.Instance);

        InventoryPass pass = await retriever.RetrieveAsync(WorkflowColumns.Inventory, CancellationToken.None);

        Assert.Equal(45, pass.Records.Count);
        Assert.Equal(45, pass.Records.Select(record => record.WorkflowId).Distinct().Count());
        List<SeenRequest> pages = [.. server.Requests.Where(request => request.Uri.AbsolutePath.EndsWith("/workflows", StringComparison.Ordinal))];
        Assert.Equal(3, pages.Count);
        Assert.All(pages, page => Assert.Contains("odata.maxpagesize=20", page.Prefer, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_Inventory_Never_Selects_Xaml()
    {
        FakeCrmServer server = new FakeOrganization().Build();
        using CrmHttpClient client = FakeOrganization.Client(server);

        await new WorkflowInventoryRetriever(client, 20, NullLogger.Instance).RetrieveAsync(WorkflowColumns.Inventory, CancellationToken.None);

        Assert.DoesNotContain(server.Requests, request => Uri.UnescapeDataString(request.Uri.Query).Contains("xaml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_Repeated_Next_Link_Aborts_Paging()
    {
        FakeCrmServer server = new FakeCrmServer().OnJson("workflows?",
            "{\"value\":[],\"@odata.nextLink\":\"" + FakeCrmServer.BaseUrl + "workflows?$skiptoken=same\"}");
        using CrmHttpClient client = FakeOrganization.Client(server);
        ODataPager pager = new(client);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ODataPage _ in pager.GetPagesAsync("workflows?$select=workflowid", 20, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task A_Transient_Status_Is_Retried_And_Then_Succeeds()
    {
        int calls = 0;
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            calls++;
            return calls < 3 ? FakeCrmServer.Json("{}", HttpStatusCode.ServiceUnavailable) : FakeCrmServer.Json("{\"ok\":true}");
        });
        List<TimeSpan> delays = [];
        using CrmHttpClient client = FakeOrganization.Client(server, delays);

        CrmResponse response = await client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(3, calls);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Retry_After_Is_Honoured()
    {
        int calls = 0;
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            calls++;
            if (calls > 1)
            {
                return FakeCrmServer.Json("{}");
            }
            HttpResponseMessage throttled = FakeCrmServer.Json("{}", HttpStatusCode.TooManyRequests);
            throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return throttled;
        });
        List<TimeSpan> delays = [];
        using CrmHttpClient client = FakeOrganization.Client(server, delays);

        await client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
    }

    [Fact]
    public async Task A_Client_Error_Is_Not_Retried_And_Fails_Loudly()
    {
        int calls = 0;
        FakeCrmServer server = new FakeCrmServer().On("workflows?", _ =>
        {
            calls++;
            return FakeCrmServer.Json("{\"error\":{\"message\":\"Could not find a property named 'x'\"}}", HttpStatusCode.BadRequest);
        });
        using CrmHttpClient client = FakeOrganization.Client(server);

        CrmRequestException error = await Assert.ThrowsAsync<CrmRequestException>(() => client.GetAsync("workflows?$select=x", CrmPreferences.None, CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Contains("Could not find a property", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_Faults_Stop_After_The_Bounded_Number_Of_Attempts()
    {
        int calls = 0;
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            calls++;
            return FakeCrmServer.Json("{}", HttpStatusCode.GatewayTimeout);
        });
        using CrmHttpClient client = FakeOrganization.Client(server);

        await Assert.ThrowsAsync<CrmRequestException>(() => client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None));

        Assert.Equal(3, calls);
    }
}
