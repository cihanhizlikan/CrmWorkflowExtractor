using System.Net;

namespace Crm.Extract.Http;

public enum CrmDeploymentKind
{
    Unknown,
    OnPremises,
    InternetFacing
}

/// <summary>
/// Reads a refused or redirected response for the signature of the deployment behind it. An on-premises server
/// challenges with <c>Negotiate</c>/<c>NTLM</c>; an IFD/claims server challenges with <c>Bearer</c> (carrying an
/// <c>authorization_uri</c>) or redirects to an AD FS sign-in page.
/// </summary>
internal static class DeploymentClassifier
{
    private static readonly string[] FederationMarkers = ["/adfs/", "wsfed", "wa=wsignin", "/oauth2/"];

    public static (CrmDeploymentKind Kind, string Evidence) Classify(CrmResponse response)
    {
        if (IsRedirect(response.StatusCode))
        {
            string target = response.Location?.ToString() ?? "";
            string? marker = FederationMarkers.FirstOrDefault(candidate => target.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            return marker is null
                ? (CrmDeploymentKind.Unknown, $"redirect to '{target}'")
                : (CrmDeploymentKind.InternetFacing, $"redirect to a federation endpoint '{target}'");
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (response.WwwAuthenticate.Any(challenge => challenge.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)))
            {
                return (CrmDeploymentKind.InternetFacing, "WWW-Authenticate: Bearer challenge");
            }
            if (response.WwwAuthenticate.Any(challenge => challenge.StartsWith("Negotiate", StringComparison.OrdinalIgnoreCase)
                || challenge.StartsWith("NTLM", StringComparison.OrdinalIgnoreCase)))
            {
                return (CrmDeploymentKind.OnPremises, "WWW-Authenticate: Negotiate/NTLM challenge");
            }
            return (CrmDeploymentKind.Unknown, "401 without a recognizable challenge");
        }
        return (CrmDeploymentKind.Unknown, "not an authentication response");
    }

    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    }
}
