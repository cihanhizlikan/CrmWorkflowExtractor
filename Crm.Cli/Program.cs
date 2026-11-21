using System.Text;
using Crm.Cli.Configuration;

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
    private const string ProcessMiningFile = "";            // e.g. "D:/exports/crm-process-mining-20261012-090000.json" (tools/crm-process-mining.html)
    private const string LogoFile = "";                     // a PNG for the guide's cover; empty keeps the built-in logo
    // -----------------------------------------------------------------------------------------------------------

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        return await StartAsync(AppContext.BaseDirectory, Console.Out, Console.Error);
    }

    /// <summary>
    /// <see cref="Main"/> with its folder and writers named, so a test can hand it a settings file of its own.
    /// </summary>
    internal static async Task<int> StartAsync(string baseDirectory, TextWriter output, TextWriter error)
    {
        ExtractorSettings settings;
        try
        {
            settings = ExtractorSettings.Bind(ExtractorSettings.BuildConfiguration(Fallback(), baseDirectory));
        }
        // Before a run folder exists there is nothing to seal, so the run's own guards cannot apply: a settings file
        // that is not JSON (InvalidDataException), a value that does not convert (InvalidOperationException) or a file
        // that cannot be opened is the operator's to fix, and they get a sentence, not a stack (sweep row T3).
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            string detail = failure.InnerException is { } inner ? $"{failure.Message} {inner.Message}" : failure.Message;
            await error.WriteLineAsync($"Ayarlar okunamadı; çalıştırma klasörü oluşturulmadı. {Path.Combine(baseDirectory, "appsettings.json")} "
                + $"dosyasını denetleyin — {detail}");
            return (int)ExitCode.ConfigurationInvalid;
        }
        if (settings.Run.Value.ReprocessRunId.Trim().Length == 0 && settings.Run.Value.ImportFile.Trim().Length == 0)
        {
            await error.WriteLineAsync("Okunacak bir şey yok; çalıştırma klasörü oluşturulmadı. appsettings.json içinde Run:ImportFile "
                + "değerine tools/crm-export.html sayfasının kaydettiği dosyayı ya da Run:ReprocessRunId değerine önceki bir "
                + "çalıştırmanın klasör adını yazın.");
            return (int)ExitCode.ConfigurationInvalid;
        }

        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        ExtractionRun run = new(settings, TimeProvider.System, output);
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
            ["Run:ProcessMiningFile"] = ProcessMiningFile,
            ["Run:LogoFile"] = LogoFile
        };
    }
}
