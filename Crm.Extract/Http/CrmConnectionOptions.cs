namespace Crm.Extract.Http;

/// <summary>How the Web API caller authenticates. On-premises deployments take network credentials only (§2.2).</summary>
public enum CrmAuthenticationMode
{
    /// <summary>Run as the process identity (the service account). No secret stored anywhere. Preferred.</summary>
    Default,

    /// <summary>Username and domain from configuration; password from the gitignored settings file or Credential Manager.</summary>
    Explicit
}

/// <summary>
/// Which Windows authentication protocol to offer. <see cref="Negotiate"/> lets Windows pick Kerberos and fall back
/// to NTLM; <see cref="Ntlm"/> skips Kerberos entirely — the standard workaround when Kerberos is misconfigured for
/// the host name (a missing or wrong SPN), which typically shows as "the browser gets in, the application gets 401".
/// </summary>
public enum CrmAuthenticationScheme
{
    Negotiate,
    Ntlm
}

/// <summary>Connection settings bound from the <c>Crm</c> configuration section.</summary>
public sealed class CrmConnectionOptions
{
    public const string SectionName = "Crm";
    public const string PasswordPlaceholder = "__CRM_PASSWORD__";

    /// <summary>The Web API root, e.g. <c>https://crm.example.local/OrgName/api/data/v8.2/</c>.</summary>
    public string WebApiBaseUrl { get; set; } = "";

    public CrmAuthenticationMode Authentication { get; set; } = CrmAuthenticationMode.Default;

    public CrmAuthenticationScheme AuthenticationScheme { get; set; } = CrmAuthenticationScheme.Negotiate;

    /// <summary>Who the server will be asked to accept, in words — for logs and for the failure message when it refuses.</summary>
    public string DescribeCredentials()
    {
        string account = Authentication == CrmAuthenticationMode.Default
            ? $"the Windows account running this process, {Environment.UserDomainName}\\{Environment.UserName} (Crm:Authentication = Default)"
            : $"{Domain}\\{UserName} from configuration (Crm:Authentication = Explicit)";
        return $"{account}, scheme {AuthenticationScheme}";
    }

    public string UserName { get; set; } = "";

    public string Domain { get; set; } = "";

    /// <summary>Never a real value in the committed <c>appsettings.json</c>; the placeholder there means "not supplied".</summary>
    public string Password { get; set; } = PasswordPlaceholder;

    /// <summary>Windows Credential Manager generic-credential target consulted when no password is configured.</summary>
    public string CredentialTarget { get; set; } = "CrmWorkflowExtractor";

    /// <summary>Sent as <c>Prefer: odata.maxpagesize</c>. Modest on purpose (§3.5).</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Total attempts per request, first try included, for transient faults.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>The configured base URL, normalized to end with a slash so relative paths resolve beneath it.</summary>
    public Uri WebApiBaseUri()
    {
        string url = WebApiBaseUrl.Trim();
        if (!url.EndsWith('/'))
        {
            url += "/";
        }
        return new Uri(url, UriKind.Absolute);
    }

    /// <summary>The password actually supplied through configuration, or null when only the placeholder or nothing is there.</summary>
    public string? ConfiguredPassword()
    {
        if (string.IsNullOrEmpty(Password) || string.Equals(Password, PasswordPlaceholder, StringComparison.Ordinal))
        {
            return null;
        }
        return Password;
    }
}
