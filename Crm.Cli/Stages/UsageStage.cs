using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Ir.Text;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>The newest run CRM still has a record of, for one definition.</summary>
public sealed record WorkflowUsage(DateTimeOffset? LastLoggedRun, string? Source, int FailedLookups);

/// <summary>A <c>crm-usage-export/1</c> file: per-definition run evidence and how far back the logs reach.</summary>
public sealed record UsageEvidence(DateTimeOffset? OldestWorkflowJob, DateTimeOffset? OldestDialogSession, string ExportedAtUtc, IReadOnlyDictionary<Guid, WorkflowUsage> Workflows);

/// <summary>
/// Whether a workflow is used. Only two things are certain: a Draft definition cannot start a run, and a logged run
/// proves the workflow ran. The absence of a log proves nothing — System Jobs are deleted routinely, real-time
/// workflows log only failures, business rules are never logged — and every verdict below says which case it is.
/// </summary>
public static partial class UsageStage
{
    public const string EvidenceFile = "raw/usage-export.json";
    public const string Format = "crm-usage-export/1";
    public const string DraftState = "Draft";

    /// <summary>
    /// Copies <c>Run:UsageFile</c> into the run as evidence when set; then reads the run's evidence copy, which a
    /// reprocessed run inherits from its source run. Null when neither exists.
    /// </summary>
    public static async Task<UsageEvidence?> LoadAsync(RunFolder folder, RunState state, string usageFile, ILogger logger, CancellationToken token)
    {
        string path = usageFile.Trim();
        if (path.Length > 0)
        {
            string file = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            if (!File.Exists(file))
            {
                throw new InvalidOperationException($"Run:UsageFile '{file}' does not exist.");
            }
            await folder.CopyVerbatimAsync(file, EvidenceFile, token);
        }
        string evidence = folder.PathOf(EvidenceFile);
        if (!File.Exists(evidence))
        {
            return null;
        }
        UsageEvidence usage = Parse(await File.ReadAllTextAsync(evidence, token));
        state.StagesRun.Add("usage");
        state.Counts["usage.definitions"] = usage.Workflows.Count;
        state.Counts["usage.withLoggedRun"] = usage.Workflows.Values.Count(entry => entry.LastLoggedRun is not null);
        int failed = usage.Workflows.Values.Sum(entry => entry.FailedLookups);
        if (failed > 0)
        {
            state.Warnings.Add($"The usage export has {failed} failed lookup(s); those workflows show no logged run for that reason, not because none exists.");
        }
        logger.LogInformation("Usage evidence exported {At}: {Definitions} definitions, {Logged} with a logged run", usage.ExportedAtUtc, usage.Workflows.Count, state.Counts["usage.withLoggedRun"]);
        return usage;
    }

    public static UsageEvidence Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetString() != Format)
        {
            throw new InvalidDataException($"The usage file is not a {Format} file (tools/crm-usage-export.js).");
        }
        Dictionary<Guid, WorkflowUsage> workflows = [];
        List<DateTimeOffset> jobRuns = [];
        List<DateTimeOffset> sessionRuns = [];
        foreach (JsonProperty entry in root.GetProperty("usage").EnumerateObject())
        {
            List<(DateTimeOffset At, string Source)> runs = [];
            int failed = 0;
            foreach ((string list, string source) in new[] { ("jobs", "system job"), ("sessions", "dialog session") })
            {
                foreach (JsonElement lookup in entry.Value.GetProperty(list).EnumerateArray())
                {
                    if (lookup.TryGetProperty("error", out _))
                    {
                        failed++;
                    }
                    else if (CreatedOn(lookup) is DateTimeOffset at)
                    {
                        runs.Add((at, source));
                        (source == "system job" ? jobRuns : sessionRuns).Add(at);
                    }
                }
            }
            (DateTimeOffset At, string Source)? newest = runs.Count == 0 ? null : runs.MaxBy(run => run.At);
            workflows[Guid.Parse(entry.Name)] = new WorkflowUsage(newest?.At, newest?.Source, failed);
        }
        // How far back the logs reach, as far as this evidence can show: the oldest run found. Asking the server for
        // the oldest record of all would mean sorting the whole System Job table, which production cannot answer.
        return new UsageEvidence(
            jobRuns.Count == 0 ? null : jobRuns.Min(),
            sessionRuns.Count == 0 ? null : sessionRuns.Min(),
            root.GetProperty("exportedAtUtc").GetString() ?? "",
            workflows);
    }

    private static DateTimeOffset? CreatedOn(JsonElement lookup)
    {
        return lookup.TryGetProperty("row", out JsonElement row) && row.ValueKind == JsonValueKind.Object
            && row.TryGetProperty("createdon", out JsonElement created) && created.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
            ? at
            : null;
    }

    /// <summary>A name that reads like a draft, a test or a copy. A hint for the reader, never a reason to drop a workflow.</summary>
    public static bool NameSuggestsTest(string name)
    {
        return TestWord().IsMatch(TurkishFold.Fold(name));
    }

    public static string Verdict(WorkflowIdentity identity, UsageEvidence? usage)
    {
        if (identity.State == DraftState)
        {
            return "Draft: cannot start new runs";
        }
        if (usage is null)
        {
            return "";
        }
        if (usage.Workflows.GetValueOrDefault(identity.WorkflowId) is { LastLoggedRun: DateTimeOffset last } found)
        {
            return $"Used: last logged run {Day(last)} ({found.Source})";
        }
        if (identity.Category == "Business Rule")
        {
            return "Unknowable: business rules run in the browser and are never logged";
        }
        if (identity.Mode == "Real-time")
        {
            return "No failure logged: real-time workflows log only failures, so this says nothing about use";
        }
        DateTimeOffset? horizon = identity.Category == "Dialog" ? usage.OldestDialogSession : usage.OldestWorkflowJob;
        return horizon is DateTimeOffset since
            ? $"No logged run; the oldest run seen anywhere is {Day(since)}: NOT proof of non-use (jobs are deleted routinely)"
            : "No logged run, and no log at all to compare with: NOT proof of non-use";
    }

    public static async Task WriteReportAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, CancellationToken token)
    {
        ExcelCsv csv = new("workflow_name", "workflow_id", "category", "mode", "state", "name_suggests_test", "last_logged_run", "evidence", "verdict");
        foreach (WorkflowIr document in documents.OrderBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            WorkflowIdentity identity = document.Identity;
            WorkflowUsage? found = usage?.Workflows.GetValueOrDefault(identity.WorkflowId);
            csv.Row(identity.Name, identity.WorkflowId, identity.Category, identity.Mode, identity.State, NameSuggestsTest(identity.Name),
                found?.LastLoggedRun is DateTimeOffset last ? Day(last) : "", found?.Source ?? "", Verdict(identity, usage));
        }
        await folder.WriteBytesAsync("reports/usage.csv", csv.ToBytes(), token);

        List<WorkflowIr> drafts = [.. documents.Where(document => document.Identity.State == DraftState)];
        List<WorkflowIr> testNamedActive = [.. documents.Where(document => document.Identity.State != DraftState && NameSuggestsTest(document.Identity.Name))];
        StringBuilder text = new();
        text.AppendLine("# Usage").AppendLine();
        text.AppendLine("Only two things are certain: a **Draft** definition cannot start a run (runs already waiting from before it was deactivated can still finish), and a **logged run** proves the workflow ran. No logged run proves nothing: System Jobs are deleted routinely, real-time workflows log only failures, and business rules are never logged.").AppendLine();
        if (usage is null)
        {
            text.AppendLine("No usage evidence in this run. Export it with `tools/crm-usage.html` and set `Run:UsageFile`.").AppendLine();
        }
        else
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Evidence exported {usage.ExportedAtUtc}. Oldest System Job seen: **{(usage.OldestWorkflowJob is DateTimeOffset job ? Day(job) : "none")}**; oldest dialog session seen: **{(usage.OldestDialogSession is DateTimeOffset session ? Day(session) : "none")}**. These are the oldest runs found, so they are a floor on how far the logs reach, not the retention setting.").AppendLine();
        }
        text.AppendLine("| Verdict | Definitions |").AppendLine("|---|---:|");
        foreach (IGrouping<string, string> group in documents.Select(document => VerdictKind(Verdict(document.Identity, usage))).GroupBy(kind => kind).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {group.Count()} |");
        }
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"**Draft definitions ({drafts.Count})** are held apart from similarity grouping and combining; each still has its IR and BPMN, and they are listed in `clusters/drafts.csv`.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"## Activated, with a name that reads like a draft or test ({testNamedActive.Count})").AppendLine();
        text.AppendLine("These can run in production, so they stay in the grouping. Read the verdict before treating any as unused.").AppendLine();
        text.AppendLine("| Workflow | Category | Mode | Verdict |").AppendLine("|---|---|---|---|");
        foreach (WorkflowIr document in testNamedActive.OrderBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {document.Identity.Name} | {document.Identity.Category} | {document.Identity.Mode} | {Verdict(document.Identity, usage)} |");
        }
        await folder.WriteTextAsync("reports/usage.md", text.ToString(), token);
        state.Counts["usage.drafts"] = drafts.Count;
        state.Counts["usage.testNamedActive"] = testNamedActive.Count;
    }

    private static string VerdictKind(string verdict)
    {
        int colon = verdict.IndexOf(':', StringComparison.Ordinal);
        return verdict.Length == 0 ? "No evidence file" : colon < 0 ? verdict : verdict[..colon];
    }

    private static string Day(DateTimeOffset at)
    {
        return at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // Folded (lower-case, ASCII) whole words: DRAFT_, 18970_DEBUG_TEST, "Deneme", "... (eski)", "Kopya - ...".
    [GeneratedRegex(@"(?<![a-z0-9])(draft|test|tst|debug|deneme|old|eski|yedek|backup|copy|kopya|temp|tmp)(?![a-z])")]
    private static partial Regex TestWord();
}
