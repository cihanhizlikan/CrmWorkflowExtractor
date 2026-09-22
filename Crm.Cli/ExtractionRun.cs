using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Cli.Configuration;
using Crm.Cli.Logging;
using Crm.Cli.Reports;
using Crm.Cli.Stages;
using Crm.Extract.Http;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;
using Crm.Extract.Runs;
using Crm.Extract.Xaml;
using Microsoft.Extensions.Logging;

namespace Crm.Cli;

/// <summary>Creates the network client for a run. Production passes <see cref="CrmHttpClient.Create"/>; tests pass a fake-backed one.</summary>
public delegate CrmHttpClient CrmClientFactory(CrmConnectionOptions options, string? password, ILogger logger);

/// <summary>
/// One end-to-end run. M1 stages: identity → privileges → column availability → <c>$count</c> + paged inventory →
/// reconciliation. Whatever happens, the run folder is sealed with a manifest — a failed run is evidence too.
/// </summary>
public sealed class ExtractionRun(ExtractorSettings settings, string? password, CrmClientFactory clientFactory, TimeProvider time, TextWriter console)
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
            catch (CrmInternetFacingDeploymentException error)
            {
                state.Fail(ExitCode.InternetFacingDeployment, error.Message);
            }
            catch (CrmAuthenticationException error)
            {
                state.Fail(ExitCode.AuthenticationRejected, error.Message);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            {
                state.Fail(ExitCode.ServerUnreachable, $"Sunucuya erişilemedi: {error.Message}");
            }
            catch (Exception error) when (error is CrmRequestException or CrmBoundaryViolationException or InvalidDataException or InvalidOperationException
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
            await WriteEvidenceAsync(folder, state, records, CancellationToken.None);
            logger.LogInformation("{Summary}", InventoryReport.Summary(state));
        }
        // A provider added by instance is NOT disposed by the logger factory. The logs must be closed — complete and
        // flushed — before the manifest hashes them.
        logProvider.Dispose();

        await folder.SealAsync(artifacts => Manifest(state, started, time.GetUtcNow(), artifacts), CancellationToken.None);
        await console.WriteAsync(InventoryReport.Summary(state));
        return state.ExitCode;
    }

    /// <summary>Where the evidence comes from: an earlier run (reprocess), a browser export (import), or the Web API.</summary>
    private async Task<IReadOnlyList<WorkflowInventoryRecord>> LoadSourceAsync(RunFolder folder, RunState state, ILogger logger, CancellationToken token)
    {
        string reprocess = settings.Run.Value.ReprocessRunId.Trim();
        if (reprocess.Length > 0)
        {
            return await Reprocessing.LoadAsync(folder, state, settings.Output.Value.ResolvedRoot(), reprocess, logger, token);
        }
        string import = settings.Run.Value.ImportFile.Trim();
        if (import.Length > 0)
        {
            return await BrowserExportImport.LoadAsync(folder, state, settings.Run.Value.RequireOrganizationReadPrivileges, import, logger, token);
        }
        return await ExecuteStagesAsync(folder, state, logger, token);
    }

    private async Task<IReadOnlyList<WorkflowInventoryRecord>> ExecuteStagesAsync(RunFolder folder, RunState state, ILogger logger, CancellationToken token)
    {
        CrmConnectionOptions crm = settings.Crm.Value;
        using CrmHttpClient client = clientFactory(crm, password, logger);
        // XAML bodies are kept as raw/xaml/ files instead; holding hundreds of them in memory is what §3.5 warns against.
        client.ResponseObserver = response =>
        {
            if (!Uri.UnescapeDataString(response.RequestUri.Query).Contains(XamlRetriever.SelectMarker, StringComparison.Ordinal))
            {
                state.Responses.Add(response);
            }
        };
        state.OrganizationUrl = client.BaseUri.ToString();

        state.Identity = await CrmIdentity.ResolveAsync(client, token);
        state.StagesRun.Add(RunStages.Identity);
        logger.LogInformation("Authenticated as {Domain} ({UserId})", state.Identity.DomainName, state.Identity.UserId);

        state.Privileges = await PrivilegeCheck.RunAsync(client, state.Identity.UserId, token);
        state.StagesRun.Add(RunStages.Privileges);
        ApplyPrivilegeVerdicts(state, settings.Run.Value.RequireOrganizationReadPrivileges);

        (IReadOnlyList<string> available, IReadOnlyList<string> missing) = await WorkflowColumns.ResolveAsync(client, token);
        state.ColumnsSelected = available;
        state.ColumnsMissing = missing;
        state.StagesRun.Add(RunStages.Columns);
        foreach (string column in missing)
        {
            state.Warnings.Add($"§3.1 listesindeki '{column}' sütunu bu sunucunun workflow varlığında yok; $select dışında bırakıldı.");
        }

        // Insufficient privilege does not stop the inventory: the retrieved count is what an administrator compares
        // against, and the run is already marked failed so its output cannot be mistaken for complete.
        WorkflowInventoryRetriever retriever = new(client, crm.PageSize, logger);
        InventoryPass pass = await retriever.RetrieveAsync(available, token);
        state.StagesRun.Add(RunStages.Inventory);
        await WriteInventoryAsync(folder, state, token);

        ReconciliationResult reconciliation = InventoryReconciliation.Evaluate(pass.ApiCount, pass.Records);
        state.Reconciliation = reconciliation;
        state.StagesRun.Add(RunStages.Reconciliation);
        state.Warnings.AddRange(reconciliation.Warnings);
        foreach (string failure in reconciliation.Failures)
        {
            state.Fail(ExitCode.RunFailed, failure);
        }
        state.Records = pass.Records;

        // A run that already failed (privileges, counts) keeps its inventory as evidence but does not go on to pull
        // every workflow's XAML: whatever it would build on is known to be incomplete.
        if (state.Failures.Count > 0)
        {
            logger.LogError("Envanterden sonra durduruldu: çalıştırma başarısız oldu ve sonraki aşamalar eksik veri üzerine kurulacaktı.");
            return pass.Records;
        }
        await RetrievalStages.RunAsync(folder, state, client, settings, logger, token);
        return pass.Records;
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

    private static async Task WriteEvidenceAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowInventoryRecord> records, CancellationToken token)
    {
        StringBuilder index = new();
        for (int sequence = 0; sequence < state.Responses.Count; sequence++)
        {
            CrmResponse response = state.Responses[sequence];
            string file = string.Create(CultureInfo.InvariantCulture, $"{RunPaths.RawHttp}/{sequence + 1:0000}.body");
            await folder.WriteVerbatimAsync(file, response.Body, token);
            index.Append(JsonSerializer.Serialize(new
            {
                sequence = sequence + 1,
                uri = response.RequestUri.ToString(),
                status = (int)response.StatusCode,
                file
            })).Append('\n');
        }
        await folder.WriteTextAsync(RunPaths.RawHttpIndex, index.ToString(), token);

        if (state.StagesRun.Contains(RunStages.Inventory))
        {
            await folder.WriteTextAsync(RunPaths.Inventory, InventoryReport.Markdown(state, records), token);
        }
        await folder.WriteTextAsync(RunPaths.Report, RunReport.Markdown(state), token);
    }

    /// <summary>Written as soon as the inventory exists: every later stage reads the records from this file, not from memory.</summary>
    private static async Task WriteInventoryAsync(RunFolder folder, RunState state, CancellationToken token)
    {
        StringBuilder lines = new();
        // The FetchXML aggregate count shares the /workflows path; its row is a count, not a workflow.
        foreach (CrmResponse page in state.Responses.Where(response => response.IsSuccess
            && response.RequestUri.AbsolutePath.EndsWith("/workflows", StringComparison.OrdinalIgnoreCase)
            && !response.RequestUri.Query.Contains("fetchXml=", StringComparison.OrdinalIgnoreCase)))
        {
            using JsonDocument document = JsonDocument.Parse(page.Body);
            foreach (JsonElement record in document.RootElement.GetProperty("value").EnumerateArray())
            {
                lines.Append(record.GetRawText().ReplaceLineEndings("")).Append('\n');
            }
        }
        await folder.WriteTextAsync(RunPaths.RawWorkflows, lines.ToString(), token);
    }

    private RunManifest Manifest(RunState state, DateTimeOffset started, DateTimeOffset ended, IReadOnlyList<RunArtifact> artifacts)
    {
        CrmConnectionOptions crm = settings.Crm.Value;
        Uri? baseUri = Uri.TryCreate(crm.WebApiBaseUrl, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        return new RunManifest(
            SchemaVersion: 1,
            RunId: state.RunId,
            Status: state.Status,
            ToolVersion: state.ToolVersion,
            StartedUtc: started,
            EndedUtc: ended,
            Server: baseUri?.Authority,
            OrganizationUrl: state.OrganizationUrl,
            AuthenticationMode: crm.Authentication.ToString(),
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

    /// <summary>SHA-256 of the effective configuration with the password removed, so two runs can be proven to share settings.</summary>
    private string ConfigurationHash()
    {
        CrmConnectionOptions crm = settings.Crm.Value;
        string json = JsonSerializer.Serialize(new
        {
            crm = new { crm.WebApiBaseUrl, Authentication = crm.Authentication.ToString(), crm.UserName, crm.Domain, crm.CredentialTarget, crm.PageSize, crm.MaxAttempts, crm.RequestTimeoutSeconds },
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
