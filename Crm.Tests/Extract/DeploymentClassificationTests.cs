using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Crm.Cli;
using Crm.Extract.Http;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>§2.2: an internet-facing deployment is detected and stops the run; no OAuth path exists.</summary>
public sealed class DeploymentClassificationTests
{
    [Fact]
    public async Task A_Bearer_Challenge_Is_An_Internet_Facing_Deployment()
    {
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            HttpResponseMessage response = FakeCrmServer.Json("", HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "authorization_uri=https://sts.test/adfs/oauth2/authorize"));
            return response;
        });
        using CrmHttpClient client = FakeOrganization.Client(server);

        await Assert.ThrowsAsync<CrmInternetFacingDeploymentException>(() => client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None));
    }

    [Fact]
    public async Task A_Redirect_To_Adfs_Is_An_Internet_Facing_Deployment()
    {
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            HttpResponseMessage response = FakeCrmServer.Json("", HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://sts.test/adfs/ls/?wa=wsignin1.0");
            return response;
        });
        using CrmHttpClient client = FakeOrganization.Client(server);

        await Assert.ThrowsAsync<CrmInternetFacingDeploymentException>(() => client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None));
    }

    [Fact]
    public async Task A_Negotiate_Challenge_Is_Rejected_Windows_Credentials()
    {
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            HttpResponseMessage response = FakeCrmServer.Json("", HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Negotiate"));
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("NTLM"));
            return response;
        });
        using CrmHttpClient client = FakeOrganization.Client(server);

        await Assert.ThrowsAsync<CrmAuthenticationException>(() => client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None));
    }

    [Fact]
    public async Task An_Internet_Facing_Deployment_Stops_The_Run_And_Still_Seals_Evidence()
    {
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            HttpResponseMessage response = FakeCrmServer.Json("", HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "authorization_uri=https://sts.test/adfs/oauth2/authorize"));
            return response;
        });
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.InternetFacingDeployment, code);
        Assert.Single(server.Requests);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("envanter", manifest.RootElement.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
    }
}
