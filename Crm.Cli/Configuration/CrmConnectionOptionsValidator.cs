using Crm.Extract.Http;
using Microsoft.Extensions.Options;

namespace Crm.Cli.Configuration;

/// <summary>Rejects a configuration that cannot possibly produce a correct run, before anything touches the network.</summary>
public sealed class CrmConnectionOptionsValidator : IValidateOptions<CrmConnectionOptions>
{
    public ValidateOptionsResult Validate(string? name, CrmConnectionOptions options)
    {
        List<string> failures = [];
        if (string.IsNullOrWhiteSpace(options.WebApiBaseUrl))
        {
            failures.Add("Crm:WebApiBaseUrl is empty. Set it in appsettings.json, e.g. https://crm.example.local/OrgName/api/data/v8.2/");
        }
        else if (!Uri.TryCreate(options.WebApiBaseUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"Crm:WebApiBaseUrl '{options.WebApiBaseUrl}' is not an absolute http(s) URL.");
        }
        else if (!uri.AbsolutePath.Contains("/api/data/v", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Crm:WebApiBaseUrl '{options.WebApiBaseUrl}' does not point at a Web API root (…/api/data/v8.2/).");
        }

        if (options.Authentication == CrmAuthenticationMode.Explicit)
        {
            if (string.IsNullOrWhiteSpace(options.UserName))
            {
                failures.Add("Crm:Authentication is Explicit but Crm:UserName is empty.");
            }
            if (string.IsNullOrWhiteSpace(options.Domain))
            {
                failures.Add("Crm:Authentication is Explicit but Crm:Domain is empty.");
            }
        }
        if (options.PageSize is < 1 or > 5000)
        {
            failures.Add("Crm:PageSize must be between 1 and 5000.");
        }
        if (options.MaxAttempts is < 1 or > 10)
        {
            failures.Add("Crm:MaxAttempts must be between 1 and 10.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
