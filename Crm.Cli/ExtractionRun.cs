using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Cli.Configuration;
using Crm.Cli.Logging;
using Crm.Cli.Reports;
using Crm.Extract.Http;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;
using Crm.Extract.Runs;
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

        RunLogProvider logProvider = new(folder.PathOf("logs/run.log"), folder.PathOf("logs/warnings.txt"));
        using (ILoggerFactory loggers = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logProvider)))
        {
            ILogger logger = loggers.CreateLogger("Crm.Run");
            logger.LogInformation("Run {RunId} started by tool {Version}", state.RunId, state.ToolVersion);
            try
            {
                records = await ExecuteStagesAsync(state, logger, token);
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
                state.Fail(ExitCode.ServerUnreachable, $"The server could not be reached: {error.Message}");
            }
            catch (Exception error) when (error is CrmRequestException or CrmBoundaryViolationException or InvalidDataException or InvalidOperationException or JsonException or KeyNotFoundException)
            {
                state.Fail(ExitCode.RunFailed, $"{error.GetType().Name}: {error.Message}");
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

    private async Task<IReadOnlyList<WorkflowInventoryRecord>> ExecuteStagesAsync(RunState state, ILogger logger, CancellationToken token)
    {
        CrmConnectionOptions crm = settings.Crm.Value;
        using CrmHttpClient client = clientFactory(crm, password, logger);
        client.ResponseObserver = state.Responses.Add;
        state.OrganizationUrl = client.BaseUri.ToString();

        state.Identity = await CrmIdentity.ResolveAsync(client, token);
        state.StagesRun.Add("identity");
        logger.LogInformation("Authenticated as {Domain} ({UserId})", state.Identity.DomainName, state.Identity.UserId);

        state.Privileges = await PrivilegeCheck.RunAsync(client, state.Identity.UserId, token);
        state.StagesRun.Add("privileges");
        ApplyPrivilegeVerdicts(state);

        (IReadOnlyList<string> available, IReadOnlyList<string> missing) = await WorkflowColumns.ResolveAsync(client, token);
        state.ColumnsSelected = available;
        state.ColumnsMissing = missing;
        state.StagesRun.Add("columns");
        foreach (string column in missing)
        {
            state.Warnings.Add($"Column '{column}' from §3.1 does not exist on this server's workflow entity; excluded from $select.");
        }

        // Insufficient privilege does not stop the inventory: the retrieved count is what an administrator compares
        // against, and the run is already marked failed so its output cannot be mistaken for complete.
        WorkflowInventoryRetriever retriever = new(client, crm.PageSize, logger);
        InventoryPass pass = await retriever.RetrieveAsync(available, token);
        state.StagesRun.Add("inventory");

        ReconciliationResult reconciliation = InventoryReconciliation.Evaluate(pass.ApiCount, pass.Records);
        state.Reconciliation = reconciliation;
        state.StagesRun.Add("reconciliation");
        state.Warnings.AddRange(reconciliation.Warnings);
        foreach (string failure in reconciliation.Failures)
        {
            state.Fail(ExitCode.RunFailed, failure);
        }
        return pass.Records;
    }

    private void ApplyPrivilegeVerdicts(RunState state)
    {
        foreach (PrivilegeFinding finding in state.Privileges)
        {
            string subject = $"{finding.Privilege.Name} ({finding.Privilege.Table})";
            if (finding.Verdict is PrivilegeVerdict.Missing or PrivilegeVerdict.Insufficient)
            {
                string message = $"{subject} is held at depth '{finding.Depth}', not organization level. Reads of this table return only part of the data (§2.4).";
                if (settings.Run.Value.RequireOrganizationReadPrivileges)
                {
                    state.Fail(ExitCode.RunFailed, message);
                }
                else
                {
                    state.Warnings.Add(message + " Run:RequireOrganizationReadPrivileges is false, so this is a warning only.");
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
            string file = string.Create(CultureInfo.InvariantCulture, $"raw/http/{sequence + 1:0000}.body");
            await folder.WriteVerbatimAsync(file, response.Body, token);
            index.Append(JsonSerializer.Serialize(new
            {
                sequence = sequence + 1,
                uri = response.RequestUri.ToString(),
                status = (int)response.StatusCode,
                file
            })).Append('\n');
        }
        await folder.WriteTextAsync("raw/http/index.jsonl", index.ToString(), token);

        if (state.StagesRun.Contains("inventory"))
        {
            StringBuilder lines = new();
            foreach (CrmResponse page in state.Responses.Where(response => response.RequestUri.AbsolutePath.EndsWith("/workflows", StringComparison.OrdinalIgnoreCase)))
            {
                using JsonDocument document = JsonDocument.Parse(page.Body);
                foreach (JsonElement record in document.RootElement.GetProperty("value").EnumerateArray())
                {
                    lines.Append(record.GetRawText().ReplaceLineEndings("")).Append('\n');
                }
            }
            await folder.WriteTextAsync("raw/workflows.jsonl", lines.ToString(), token);
            await folder.WriteTextAsync("reports/inventory.md", InventoryReport.Markdown(state, records), token);
        }
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
