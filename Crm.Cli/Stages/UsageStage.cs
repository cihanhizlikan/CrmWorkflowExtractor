using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Ir.Text;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>The newest run CRM still has a record of, for one definition.</summary>
public sealed record WorkflowUsage(DateTimeOffset? LastLoggedRun, string? Source, int FailedLookups);

/// <summary>A <c>crm-usage-export/1</c> file: per-definition run evidence and how far back the logs reach.</summary>
/// <summary>
/// What the export found, and how far back it was able to look. <see cref="JobsScannedSince"/> and
/// <see cref="SessionsScannedSince"/> are set only when that source was read with a bounded aggregate: then
/// "no logged run" means "nothing since that date", which is a different sentence and has to be said as one.
/// </summary>
public sealed record UsageEvidence(DateTimeOffset? OldestWorkflowJob, DateTimeOffset? OldestDialogSession,
    DateTimeOffset? JobsScannedSince, DateTimeOffset? SessionsScannedSince,
    string ExportedAtUtc, IReadOnlyDictionary<Guid, WorkflowUsage> Workflows);

/// <summary>
/// Whether a workflow is used. Only two things are certain: a Draft definition cannot start a run, and a logged run
/// proves the workflow ran. The absence of a log proves nothing — System Jobs are deleted routinely, real-time
/// workflows log only failures, business rules are never logged — and every verdict below says which case it is.
/// </summary>
public static partial class UsageStage
{
    public const string EvidenceFile = RunPaths.RawUsageExport;
    public const string Format = "crm-usage-export/1";
    public const string DraftState = ProcessLabels.StateDraft;

    /// <summary>What kind of record proved a run; also the wording the reports print.</summary>
    public const string SourceSystemJob = "sistem işi";
    public const string SourceDialogSession = "diyalog oturumu";

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
                throw new InvalidOperationException($"Run:UsageFile '{file}' bulunamadı.");
            }
            await folder.CopyVerbatimAsync(file, EvidenceFile, token);
        }
        string evidence = folder.PathOf(EvidenceFile);
        if (!File.Exists(evidence))
        {
            return null;
        }
        UsageEvidence usage = Parse(await File.ReadAllTextAsync(evidence, token));
        state.StagesRun.Add(RunStages.Usage);
        state.Counts["usage.definitions"] = usage.Workflows.Count;
        state.Counts["usage.withLoggedRun"] = usage.Workflows.Values.Count(entry => entry.LastLoggedRun is not null);
        int failed = usage.Workflows.Values.Sum(entry => entry.FailedLookups);
        if (failed > 0)
        {
            state.Warnings.Add($"Kullanım dışa aktarımında {failed} sorgu başarısız oldu; o iş akışlarında kayıtlı çalışma görünmemesinin nedeni budur, kayıt olmaması değil.");
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
            throw new InvalidDataException($"Kullanım dosyası bir {Format} dosyası değil (tools/crm-usage-export.js).");
        }
        Dictionary<Guid, WorkflowUsage> workflows = [];
        List<DateTimeOffset> jobRuns = [];
        List<DateTimeOffset> sessionRuns = [];
        foreach (JsonProperty entry in root.GetProperty("usage").EnumerateObject())
        {
            List<(DateTimeOffset At, string Source)> runs = [];
            int failed = 0;
            foreach ((string list, string source) in new[] { ("jobs", SourceSystemJob), ("sessions", SourceDialogSession) })
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
                        (source == SourceSystemJob ? jobRuns : sessionRuns).Add(at);
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
            ScannedSince(root, "jobsSinceUtc"),
            ScannedSince(root, "sessionsSinceUtc"),
            root.GetProperty("exportedAtUtc").GetString() ?? "",
            workflows);
    }

    /// <summary>
    /// How far back that source was scanned, or null when it was not bounded at all — the export declares a date
    /// only for a source it answered with an aggregate over a window.
    /// </summary>
    private static DateTimeOffset? ScannedSince(JsonElement root, string property)
    {
        return root.TryGetProperty("lookback", out JsonElement lookback) && lookback.ValueKind == JsonValueKind.Object
            && lookback.TryGetProperty(property, out JsonElement since) && since.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(since.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
            ? at
            : null;
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

    /// <summary>
    /// The same finding as <see cref="Verdict"/> in a few words, for a cell and for a diagram's header. Whoever
    /// shows it is responsible for saying once, nearby, that an absent record proves nothing.
    /// </summary>
    public static string ShortVerdict(WorkflowIdentity identity, UsageEvidence? usage)
    {
        if (identity.State == DraftState)
        {
            return "taslak, çalışamaz";
        }
        if (usage is null)
        {
            return "";
        }
        if (usage.Workflows.GetValueOrDefault(identity.WorkflowId) is { LastLoggedRun: DateTimeOffset last })
        {
            return "çalışıyor · " + Day(last);
        }
        if (identity.Category == ProcessLabels.CategoryBusinessRule)
        {
            return "bilinemez (iş kuralı iz bırakmaz)";
        }
        if (identity.Mode == ProcessLabels.ModeRealTime)
        {
            return "bilinemez (gerçek zamanlı)";
        }
        return ScannedSince(identity, usage) is DateTimeOffset from ? $"{Day(from)} sonrası çalışma yok" : "kayıtlı çalışma yok";
    }

    public static string Verdict(WorkflowIdentity identity, UsageEvidence? usage)
    {
        if (identity.State == DraftState)
        {
            return "Taslak: yeni çalıştırma başlatamaz";
        }
        if (usage is null)
        {
            return "";
        }
        if (usage.Workflows.GetValueOrDefault(identity.WorkflowId) is { LastLoggedRun: DateTimeOffset last } found)
        {
            return $"Kullanılıyor: son kayıtlı çalışma {Day(last)} ({found.Source})";
        }
        if (identity.Category == ProcessLabels.CategoryBusinessRule)
        {
            return "Bilinemez: iş kuralları tarayıcıda çalışır ve hiç kayıt bırakmaz";
        }
        if (identity.Mode == ProcessLabels.ModeRealTime)
        {
            return "Hata kaydı yok: gerçek zamanlı akışlar yalnızca hatayı kaydeder, bu bilgi kullanım hakkında bir şey söylemez";
        }
        if (ScannedSince(identity, usage) is DateTimeOffset from)
        {
            return $"Kayıtlı çalışma yok: yalnızca {Day(from)} tarihinden bugüne bakıldı, daha eskisi TARANMADI";
        }
        DateTimeOffset? horizon = identity.Category == ProcessLabels.CategoryDialog ? usage.OldestDialogSession : usage.OldestWorkflowJob;
        return horizon is DateTimeOffset since
            ? $"Kayıtlı çalışma yok; görülen en eski çalışma {Day(since)}: kullanılmadığının KANITI DEĞİLDİR (sistem işleri düzenli olarak silinir)"
            : "Kayıtlı çalışma yok ve karşılaştırılacak hiç kayıt bulunamadı: kullanılmadığının KANITI DEĞİLDİR";
    }

    /// <summary>A dialog's evidence is its sessions, everything else's is its system jobs; each has its own window.</summary>
    private static DateTimeOffset? ScannedSince(WorkflowIdentity identity, UsageEvidence? usage)
    {
        return identity.Category == ProcessLabels.CategoryDialog ? usage?.SessionsScannedSince : usage?.JobsScannedSince;
    }

    /// <summary>What the reports say about usage. The per-workflow verdict travels in the plan's own column.</summary>
    public static void Summarize(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        List<WorkflowIr> drafts = [.. documents.Where(document => document.Identity.State == DraftState)];
        List<WorkflowIr> testNamedActive = [.. documents.Where(document => document.Identity.State != DraftState && NameSuggestsTest(document.Identity.Name))];
        state.UsageVerdicts = documents.GroupBy(document => VerdictKind(Verdict(document.Identity, usage)))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        state.UsageHorizon = usage is null
            ? ""
            : string.Create(CultureInfo.InvariantCulture,
                $"Kanıt {usage.ExportedAtUtc} tarihinde alındı. {Window(usage)}Görülen en eski sistem işi: {(usage.OldestWorkflowJob is DateTimeOffset job ? Day(job) : "yok")}; "
                + $"en eski diyalog oturumu: {(usage.OldestDialogSession is DateTimeOffset session ? Day(session) : "yok")}. Bunlar bulunan en eski çalışmalardır, saklama ayarı değildir.");
        state.Counts["usage.drafts"] = drafts.Count;
        state.Counts["usage.testNamedActive"] = testNamedActive.Count;
    }

    /// <summary>
    /// The scanned window, said before any date a reader might otherwise take for the beginning of the record.
    /// Grouping the whole System Job table did not come back on the production server, so the export bounds it.
    /// </summary>
    private static string Window(UsageEvidence usage)
    {
        if (usage.JobsScannedSince is null && usage.SessionsScannedSince is null)
        {
            return "";
        }
        string jobs = usage.JobsScannedSince is DateTimeOffset from ? Day(from) + " sonrası" : "tamamı";
        string sessions = usage.SessionsScannedSince is DateTimeOffset since ? Day(since) + " sonrası" : "tamamı";
        return $"TARANAN ARALIK — sistem işleri: {jobs}; diyalog oturumları: {sessions}. Bu tarihten eski çalışmalar görülmedi. ";
    }

    private static string VerdictKind(string verdict)
    {
        int colon = verdict.IndexOf(':', StringComparison.Ordinal);
        return verdict.Length == 0 ? "Kanıt dosyası yok" : colon < 0 ? verdict : verdict[..colon];
    }

    private static string Day(DateTimeOffset at)
    {
        return at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // Folded (lower-case, ASCII) whole words: DRAFT_, 18970_DEBUG_TEST, "Deneme", "... (eski)", "Kopya - ...".
    [GeneratedRegex(@"(?<![a-z0-9])(draft|test|tst|debug|deneme|old|eski|yedek|backup|copy|kopya|temp|tmp)(?![a-z])")]
    private static partial Regex TestWord();
}
