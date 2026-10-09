using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Cli.Configuration;
using Crm.Cli.Logging;
using Crm.Cli.Reports;
using Crm.Cli.Stages;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;
using Crm.Extract.Runs;
using Microsoft.Extensions.Logging;

namespace Crm.Cli;

/// <summary>
/// One end-to-end run, from a file: an export the browser script saved, or an earlier run reprocessed. There is no
/// network path — it went on 2026-10-09, after the data had come from the browser for weeks — so nothing here can
/// reach a server. Whatever happens, the run folder is sealed with a manifest — a failed run is evidence too.
/// </summary>
public sealed class ExtractionRun(ExtractorSettings settings, TimeProvider time, TextWriter console)
{
    public async Task<ExitCode> RunAsync(CancellationToken token)
    {
        DateTimeOffset started = time.GetUtcNow();
        RunFolder folder = RunFolder.Create(settings.Output.Value.ResolvedRoot(), started);
        RunState state = new(folder.RunId, folder.Root, ToolVersion());
        IReadOnlyList<WorkflowInventoryRecord> records = [];

        RunLogProvider logProvider = new(folder.PathOf(RunPaths.RunLog), folder.PathOf(RunPaths.WarningLog));
        using (ILoggerFactory loggers = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logProvider)))
        {
            ILogger logger = loggers.CreateLogger("Crm.Run");
            logger.LogInformation("Run {RunId} started by tool {Version}", state.RunId, state.ToolVersion);
            try
            {
                records = await LoadSourceAsync(folder, state, logger, token);
                if (state.Failures.Count == 0 && state.StagesRun.Contains(RunStages.Xaml))
                {
                    await OfflineStages.RunAsync(folder, state, settings, started, logger, token);
                }
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException
                or JsonException or KeyNotFoundException or IOException or UnauthorizedAccessException)
            {
                state.Fail(ExitCode.RunFailed, $"{error.GetType().Name}: {error.Message}");
            }

            state.CountChain = CountChain.Evaluate(state);
            foreach (CountLink gap in state.CountChain.Where(link => !link.Holds))
            {
                state.Fail(ExitCode.RunFailed, "Sayım zincirinde açıklanamayan boşluk — " + gap);
            }

            foreach (string warning in state.Warnings)
            {
                logger.LogWarning("{Warning}", warning);
            }
            foreach (string failure in state.Failures)
            {
                logger.LogError("{Failure}", failure);
            }
            await WriteEvidenceAsync(folder, state, CancellationToken.None);
            logger.LogInformation("{Summary}", InventoryReport.Summary(state));
        }
        // A provider added by instance is NOT disposed by the logger factory. The logs must be closed — complete and
        // flushed — before the manifest hashes them.
        logProvider.Dispose();

        await folder.SealAsync(artifacts => Manifest(state, started, time.GetUtcNow(), artifacts), CancellationToken.None);
        await console.WriteAsync(InventoryReport.Summary(state));
        return state.ExitCode;
    }

    /// <summary>Where the evidence comes from: an earlier run (reprocess) or a browser export (import).</summary>
    private async Task<IReadOnlyList<WorkflowInventoryRecord>> LoadSourceAsync(RunFolder folder, RunState state, ILogger logger, CancellationToken token)
    {
        string reprocess = settings.Run.Value.ReprocessRunId.Trim();
        if (reprocess.Length > 0)
        {
            return await Reprocessing.LoadAsync(folder, state, settings.Output.Value.ResolvedRoot(), reprocess, logger, token);
        }
        string import = settings.Run.Value.ImportFile.Trim();
        if (import.Length == 0)
        {
            throw new InvalidOperationException("Okunacak bir şey yok: Run:ImportFile ya da Run:ReprocessRunId verilmeli.");
        }
        return await BrowserExportImport.LoadAsync(folder, state, settings.Run.Value.RequireOrganizationReadPrivileges, import, logger, token);
    }

    internal static void ApplyPrivilegeVerdicts(RunState state, bool requireOrganizationRead)
    {
        foreach (PrivilegeFinding finding in state.Privileges)
        {
            string subject = $"{finding.Privilege.Name} ({finding.Privilege.Table})";
            if (finding.Verdict is PrivilegeVerdict.Missing or PrivilegeVerdict.Insufficient)
            {
                string message = $"{subject} yetkisi '{finding.Depth}' derinliğinde, kuruluş düzeyinde değil. Bu tablodan yapılan okumalar verinin yalnızca bir bölümünü döndürür (§2.4).";
                if (requireOrganizationRead)
                {
                    state.Fail(ExitCode.RunFailed, message);
                }
                else
                {
                    state.Warnings.Add(message + " Run:RequireOrganizationReadPrivileges kapalı olduğundan bu yalnızca bir uyarıdır.");
                }
            }
            else if (finding.Verdict == PrivilegeVerdict.Unverifiable)
            {
                state.Warnings.Add($"{subject}: no privilege of that name exists on this server, so its depth could not be checked.");
            }
        }
    }

    private static async Task WriteEvidenceAsync(RunFolder folder, RunState state, CancellationToken token)
    {
        await folder.WriteTextAsync(RunPaths.Report, RunReport.Markdown(state), token);
    }

    private RunManifest Manifest(RunState state, DateTimeOffset started, DateTimeOffset ended, IReadOnlyList<RunArtifact> artifacts)
    {
        Uri? baseUri = Uri.TryCreate(state.OrganizationUrl, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        return new RunManifest(
            SchemaVersion: 1,
            RunId: state.RunId,
            Status: state.Status,
            ToolVersion: state.ToolVersion,
            StartedUtc: started,
            EndedUtc: ended,
            Server: baseUri?.Authority,
            OrganizationUrl: state.OrganizationUrl,
            // The data was read through the user's own signed-in browser session; the tool authenticated to nothing.
            AuthenticationMode: "browser-session",
            AuthenticatedUser: RunManifest.UserOf(state.Identity),
            ConfigurationSha256: ConfigurationHash(),
            StagesRun: [.. state.StagesRun],
            ColumnsSelected: state.ColumnsSelected,
            ColumnsMissingFromServer: state.ColumnsMissing,
            Privileges: RunManifest.PrivilegesOf(state.Privileges),
            Counts: state.Reconciliation?.Counts,
            StageCounts: state.Counts,
            CountChain: [.. state.CountChain.Select(link => link.ToString())],
            Failures: [.. state.Failures],
            Warnings: [.. state.Warnings],
            Artifacts: artifacts);
    }

    /// <summary>SHA-256 of the effective configuration, so two runs can be proven to share settings.</summary>
    private string ConfigurationHash()
    {
        string json = JsonSerializer.Serialize(new
        {
            output = new { settings.Output.Value.Root },
            run = new { settings.Run.Value.RequireOrganizationReadPrivileges }
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string ToolVersion()
    {
        return typeof(ExtractionRun).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }
}
