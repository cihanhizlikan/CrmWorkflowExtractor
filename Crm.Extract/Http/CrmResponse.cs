using System.Net;

namespace Crm.Extract.Http;

/// <summary>A fully-read Web API response. The body is materialized, so nothing holds a connection open.</summary>
public sealed record CrmResponse(
    Uri RequestUri,
    HttpStatusCode StatusCode,
    string Body,
    IReadOnlyList<string> WwwAuthenticate,
    Uri? Location,
    TimeSpan? RetryAfter)
{
    public bool IsSuccess
    {
        get { return (int)StatusCode is >= 200 and <= 299; }
    }

    internal static async Task<CrmResponse> ReadAsync(Uri requestUri, HttpResponseMessage response, CancellationToken token)
    {
        string body = await response.Content.ReadAsStringAsync(token);
        List<string> challenges = [.. response.Headers.WwwAuthenticate.Select(header => header.ToString())];
        TimeSpan? retryAfter = response.Headers.RetryAfter switch
        {
            { Delta: TimeSpan delta } => delta,
            { Date: DateTimeOffset date } => date - DateTimeOffset.UtcNow,
            _ => null
        };
        return new CrmResponse(requestUri, response.StatusCode, body, challenges, response.Headers.Location, retryAfter);
    }
}

/// <summary>The Web API answered with a non-success status that is not an authentication or deployment problem.</summary>
public sealed class CrmRequestException(CrmResponse response)
    : Exception($"GET {response.RequestUri} returned {(int)response.StatusCode} {response.StatusCode}: {Snippet(response.Body)}")
{
    public CrmResponse Response { get; } = response;

    private static string Snippet(string body)
    {
        string flat = body.ReplaceLineEndings(" ");
        return flat.Length <= 400 ? flat : flat[..400] + "…";
    }
}

/// <summary>Windows authentication was rejected by an on-premises server: wrong account, domain, password — or a Kerberos fault.</summary>
public sealed class CrmAuthenticationException(CrmResponse response, string credentials)
    : Exception($"GET {response.RequestUri} was refused ({(int)response.StatusCode}); the server offers Windows authentication "
        + $"({string.Join(", ", response.WwwAuthenticate)}) and did not accept {credentials}. "
        + "If a browser on this machine opens the same URL without asking for a password, retry with "
        + "Crm:AuthenticationScheme = Ntlm (Kerberos misconfigured for this host name). If the browser asked for a "
        + "user name and password, this account is not the one CRM authorized: use Crm:Authentication = Explicit.")
{
    public CrmResponse Response { get; } = response;
}

/// <summary>
/// The server behaves like an internet-facing (IFD / claims) deployment. §2.2: stop and report, never improvise OAuth.
/// </summary>
public sealed class CrmInternetFacingDeploymentException(CrmResponse response, string evidence)
    : Exception($"GET {response.RequestUri} looks like an internet-facing deployment ({evidence}). "
        + "This tool supports on-premises Windows authentication only; stopping as the handout requires.")
{
    public CrmResponse Response { get; } = response;
}

/// <summary>Something tried to leave the read-only, same-organization boundary of <see cref="CrmHttpClient"/>.</summary>
public sealed class CrmBoundaryViolationException(string message) : Exception(message);
