using System.Net;
using System.Net.Http.Headers;
using Crm.Extract.Http;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>Which credentials go on the wire, and what a refusal says about them. The live 401 of 2026-09-22 is why.</summary>
public sealed class CredentialTests
{
    [Fact]
    public void Negotiate_With_The_Process_Account_Uses_The_Handlers_Default_Credentials()
    {
        using HttpClientHandler handler = new();

        CrmHttpClient.ConfigureCredentials(handler, Options(CrmAuthenticationMode.Default, CrmAuthenticationScheme.Negotiate), null);

        Assert.True(handler.UseDefaultCredentials);
    }

    [Fact]
    public void Ntlm_Binds_The_Process_Account_To_Ntlm_For_This_Server_Only()
    {
        using HttpClientHandler handler = new();

        CrmHttpClient.ConfigureCredentials(handler, Options(CrmAuthenticationMode.Default, CrmAuthenticationScheme.Ntlm), null);

        Assert.False(handler.UseDefaultCredentials);
        ICredentials credentials = Assert.IsType<CredentialCache>(handler.Credentials);
        Assert.Same(CredentialCache.DefaultNetworkCredentials, credentials.GetCredential(new Uri("https://crm.test/"), "NTLM"));
        Assert.Null(credentials.GetCredential(new Uri("https://crm.test/"), "Negotiate"));
        Assert.Null(credentials.GetCredential(new Uri("https://elsewhere.test/"), "NTLM"));
    }

    [Fact]
    public void Ntlm_With_An_Explicit_Account_Sends_That_Account()
    {
        using HttpClientHandler handler = new();
        CrmConnectionOptions options = Options(CrmAuthenticationMode.Explicit, CrmAuthenticationScheme.Ntlm);
        options.UserName = "svc-crm-read";
        options.Domain = "CORP";

        CrmHttpClient.ConfigureCredentials(handler, options, "not-a-real-password");

        NetworkCredential? sent = handler.Credentials!.GetCredential(new Uri("https://crm.test/"), "NTLM");
        Assert.Equal("svc-crm-read", sent?.UserName);
        Assert.Equal("CORP", sent?.Domain);
    }

    /// <summary>The 2026-09-22 failure said only "did not accept the configured credentials". It now says whose, and what to try.</summary>
    [Fact]
    public async Task A_Refusal_Names_The_Account_The_Scheme_And_The_Next_Step()
    {
        FakeCrmServer server = new FakeCrmServer().On("WhoAmI()", _ =>
        {
            HttpResponseMessage response = FakeCrmServer.Json("", HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Negotiate"));
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("NTLM"));
            return response;
        });
        using CrmHttpClient client = FakeOrganization.Client(server);

        CrmAuthenticationException error = await Assert.ThrowsAsync<CrmAuthenticationException>(() => client.GetAsync("WhoAmI()", CrmPreferences.None, CancellationToken.None));

        Assert.Contains($"{Environment.UserDomainName}\\{Environment.UserName}", error.Message, StringComparison.Ordinal);
        Assert.Contains("scheme Negotiate", error.Message, StringComparison.Ordinal);
        Assert.Contains("Crm:AuthenticationScheme = Ntlm", error.Message, StringComparison.Ordinal);
    }

    private static CrmConnectionOptions Options(CrmAuthenticationMode mode, CrmAuthenticationScheme scheme)
    {
        return new CrmConnectionOptions { WebApiBaseUrl = "https://crm.test/api/data/v8.2/", Authentication = mode, AuthenticationScheme = scheme };
    }
}
