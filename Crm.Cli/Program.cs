using System.Text;
using Crm.Cli.Configuration;
using Crm.Extract.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Crm.Cli;

/// <summary>Console host. Runs with zero command-line arguments (§10); every setting comes from configuration.</summary>
public static class Program
{
    // ---- Fallback configuration (§10) ------------------------------------------------------------------------
    // Used for any key that appsettings.json, appsettings.Development.json and CRMEXTRACT_* environment variables
    // leave unset. Edit these when even editing a config file on the target host is awkward. NEVER put a password here.
    private const string WebApiBaseUrl = "";                // e.g. "https://crm.example.local/OrgName/api/data/v8.2/"
    private const string Authentication = "Default";        // "Default" (run as service account) or "Explicit"
    private const string AuthenticationScheme = "Negotiate"; // "Negotiate" or "Ntlm" (when Kerberos is broken for the host)
    private const string UserName = "";
    private const string Domain = "";
    private const string CredentialTarget = "CrmWorkflowExtractor";
    private const string PageSize = "20";
    private const string MaxAttempts = "5";
    private const string RequestTimeoutSeconds = "300";
    private const string OutputRoot = "out";
    private const string RequireOrganizationReadPrivileges = "true";
    private const string ReprocessRunId = "";               // e.g. "20260915-101500": rebuild from that run's raw/, no network
    private const string ImportFile = "";                   // e.g. "D:/exports/crm-export-20260922-150000.json" (tools/crm-export.html)
    private const string UsageFile = "";                    // e.g. "D:/exports/crm-usage-20260923-090000.json" (tools/crm-usage.html)
    // -----------------------------------------------------------------------------------------------------------

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        IConfigurationRoot configuration = ExtractorSettings.BuildConfiguration(Fallback(), AppContext.BaseDirectory);
        ExtractorSettings settings = ExtractorSettings.Bind(configuration);
        if (settings.Run.Value.ReprocessRunId.Trim().Length > 0 || settings.Run.Value.ImportFile.Trim().Length > 0)
        {
            // Reprocessing and importing read files and never contact the server, so no connection settings are needed.
            return await RunAsync(settings, null);
        }

        ValidateOptionsResult validation = new CrmConnectionOptionsValidator().Validate(Options.DefaultName, settings.Crm.Value);
        if (validation.Failed)
        {
            await Console.Error.WriteLineAsync("Configuration is invalid; nothing was contacted and no run folder was created:");
            foreach (string failure in validation.Failures)
            {
                await Console.Error.WriteLineAsync("  - " + failure);
            }
            return (int)ExitCode.ConfigurationInvalid;
        }

        string? password = null;
        CrmConnectionOptions crm = settings.Crm.Value;
        if (crm.Authentication == CrmAuthenticationMode.Explicit)
        {
            password = crm.ConfiguredPassword() ?? WindowsCredentialStore.TryReadPassword(crm.CredentialTarget);
            if (password is null)
            {
                await Console.Error.WriteLineAsync($"Crm:Authentication is Explicit but no password was found: appsettings.Development.json has none "
                    + $"and Windows Credential Manager has no generic credential named '{crm.CredentialTarget}'.");
                return (int)ExitCode.ConfigurationInvalid;
            }
        }

        return await RunAsync(settings, password);
    }

    private static async Task<int> RunAsync(ExtractorSettings settings, string? password)
    {
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        ExtractionRun run = new(settings, password, CrmHttpClient.Create, TimeProvider.System, Console.Out);
        ExitCode code = await run.RunAsync(cancellation.Token);
        return (int)code;
    }

    private static Dictionary<string, string?> Fallback()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Crm:WebApiBaseUrl"] = WebApiBaseUrl,
            ["Crm:Authentication"] = Authentication,
            ["Crm:AuthenticationScheme"] = AuthenticationScheme,
            ["Crm:UserName"] = UserName,
            ["Crm:Domain"] = Domain,
            ["Crm:CredentialTarget"] = CredentialTarget,
            ["Crm:PageSize"] = PageSize,
            ["Crm:MaxAttempts"] = MaxAttempts,
            ["Crm:RequestTimeoutSeconds"] = RequestTimeoutSeconds,
            ["Output:Root"] = OutputRoot,
            ["Run:RequireOrganizationReadPrivileges"] = RequireOrganizationReadPrivileges,
            ["Run:ReprocessRunId"] = ReprocessRunId,
            ["Run:ImportFile"] = ImportFile,
            ["Run:UsageFile"] = UsageFile
        };
    }
}
