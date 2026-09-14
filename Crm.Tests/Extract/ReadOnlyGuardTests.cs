using System.Reflection;
using Crm.Extract.Http;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>§2.3: no verb other than GET can leave <see cref="CrmHttpClient"/>, and nothing leaves the organization root.</summary>
public sealed class ReadOnlyGuardTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("MERGE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void The_Send_Guard_Rejects_Every_Verb_But_Get(string verb)
    {
#pragma warning disable RS0030 // The test must construct the forbidden verbs to prove the guard refuses them.
        using HttpRequestMessage request = new(new HttpMethod(verb), FakeCrmServer.BaseUrl + "workflows");
#pragma warning restore RS0030

        Assert.Throws<CrmBoundaryViolationException>(() => CrmHttpClient.EnsureReadOnly(request));
    }

    [Fact]
    public void The_Send_Guard_Admits_Get()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, FakeCrmServer.BaseUrl + "workflows");

        CrmHttpClient.EnsureReadOnly(request);
    }

    /// <summary>
    /// The only member of the network assembly that accepts a verb or a request is the guard itself. A second one —
    /// say a convenience <c>SendAsync(HttpMethod, …)</c> — would be a way for a verb to escape the wrapper.
    /// </summary>
    [Fact]
    public void No_Member_Of_The_Network_Assembly_Accepts_A_Verb_Or_A_Request_Except_The_Guard()
    {
        Type[] forbidden = [typeof(HttpMethod), typeof(HttpRequestMessage), typeof(HttpContent)];
        const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        List<string> offenders = [];
        foreach (Type type in typeof(CrmHttpClient).Assembly.GetTypes())
        {
            foreach (MethodBase member in type.GetMethods(everything).Cast<MethodBase>().Concat(type.GetConstructors(everything)))
            {
                bool takesForbidden = member.GetParameters().Any(parameter => forbidden.Contains(parameter.ParameterType));
                bool isGuard = type == typeof(CrmHttpClient) && member.Name == nameof(CrmHttpClient.EnsureReadOnly);
                if (takesForbidden && !isGuard)
                {
                    offenders.Add($"{type.FullName}.{member.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task Everything_A_Full_Inventory_Sends_Is_Get()
    {
        FakeCrmServer server = new FakeOrganization().Build();
        using TemporaryOutput output = new();

        await RunHarness.RunAsync(server, output);

        Assert.NotEmpty(server.Requests);
        Assert.All(server.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Theory]
    [InlineData("https://evil.test/Org/api/data/v8.2/workflows")]
    [InlineData("https://crm.test/OtherOrg/api/data/v8.2/workflows")]
    [InlineData("http://crm.test/Org/api/data/v8.2/workflows")]
    [InlineData("../../OtherOrg/api/data/v8.2/workflows")]
    [InlineData("/Org/api/data/v8.2/workflows")]
    public void A_Target_Outside_The_Organization_Root_Is_Refused(string target)
    {
        using CrmHttpClient client = FakeOrganization.Client(new FakeCrmServer());

        Assert.Throws<CrmBoundaryViolationException>(() => client.Resolve(target));
    }

    [Fact]
    public void A_Next_Link_Beneath_The_Root_Is_Followed()
    {
        using CrmHttpClient client = FakeOrganization.Client(new FakeCrmServer());

        Uri resolved = client.Resolve(FakeCrmServer.BaseUrl + "workflows?$skiptoken=page2");

        Assert.Equal(FakeCrmServer.BaseUrl + "workflows?$skiptoken=page2", resolved.ToString());
    }
}
