using System.Text;
using Crm.Cli.Configuration;
using Microsoft.Extensions.Configuration;

namespace Crm.Cli;

/// <summary>
/// Console host. Runs with zero command-line arguments (§10); every setting comes from configuration.
///
/// <para>
/// The tool reads FILES and nothing else. It once fetched from CRM itself; the organization signs in through AD FS,
/// the tool's Windows authentication was refused, and the data has come from the browser scripts in <c>tools/</c> ever
/// since (2026-09-22). The network path went on 2026-10-09: no client, no credentials, no connection settings. A run
/// either imports an export or reprocesses an earlier run, and there is nothing in it that could reach a server.
/// </para>
/// </summary>
public static class Program
{
    // ---- Fallback configuration (§10) ------------------------------------------------------------------------
    // Used for any key that appsettings.json, appsettings.Development.json and CRMEXTRACT_* environment variables
    // leave unset. Edit these when even editing a config file on the target host is awkward.
    private const string OutputRoot = "out";
    private const string RequireOrganizationReadPrivileges = "true";
    private const string ReprocessRunId = "";               // e.g. "20260915-101500": rebuild from that run's ham/
    private const string ImportFile = "";                   // e.g. "D:/exports/crm-export-20260922-150000.json" (tools/crm-export.html)
    private const string UsageFile = "";                    // e.g. "D:/exports/crm-usage-20260923-090000.json" (tools/crm-usage.html)
    private const string LogoFile = "";                     // a PNG for the guide's cover; empty keeps the built-in logo
    // -----------------------------------------------------------------------------------------------------------

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        IConfigurationRoot configuration = ExtractorSettings.BuildConfiguration(Fallback(), AppContext.BaseDirectory);
        ExtractorSettings settings = ExtractorSettings.Bind(configuration);
        if (settings.Run.Value.ReprocessRunId.Trim().Length == 0 && settings.Run.Value.ImportFile.Trim().Length == 0)
        {
            await Console.Error.WriteLineAsync("Nothing to read; no run folder was created. Set Run:ImportFile in appsettings.json to "
                + "the file tools/crm-export.html saved, or Run:ReprocessRunId to an earlier run's folder name.");
            return (int)ExitCode.ConfigurationInvalid;
        }

        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        ExtractionRun run = new(settings, TimeProvider.System, Console.Out);
        ExitCode code = await run.RunAsync(cancellation.Token);
        return (int)code;
    }

    private static Dictionary<string, string?> Fallback()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Output:Root"] = OutputRoot,
            ["Run:RequireOrganizationReadPrivileges"] = RequireOrganizationReadPrivileges,
            ["Run:ReprocessRunId"] = ReprocessRunId,
            ["Run:ImportFile"] = ImportFile,
            ["Run:UsageFile"] = UsageFile,
            ["Run:LogoFile"] = LogoFile
        };
    }
}
