using System.Globalization;
using System.Text.Json;
using Crm.Bpmn.Stages;
using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>How often cases entered one stage, and how many cases' last recorded stage it is.</summary>
public sealed record MinedStage(Guid Stage, int Entries, int LastRecorded);

/// <summary>
/// One step from a stage to the next, how often cases took it, and how long it took — calendar hours from entering
/// the first to entering the second, and the same span in working days: the time on working days, over 24. The
/// durations are absent below the export's minimum group, and the working days in a file made before they existed.
/// </summary>
public sealed record MinedStep(Guid From, Guid To, int Count, double? MeanHours, double? MedianHours, double? P90Hours)
{
    public double? MeanWorkDays { get; init; }

    public double? MedianWorkDays { get; init; }

    public double? P90WorkDays { get; init; }
}

/// <summary>A <c>crm-process-mining-export/1</c> file: totals only, reduced in the browser, with no case in it.</summary>
public sealed record ProcessMining(string? Source, string? ChosenBy, IReadOnlyList<string> Candidates, int MinimumGroup,
    string? FromDate, string? ToDate, int Cases, int Events, int Skipped, IReadOnlyList<MinedStage> Stages, IReadOnlyList<MinedStep> Steps)
{
    /// <summary>How working time was measured: where the holidays came from (null when only weekends were left out), how many.</summary>
    public WorkingTime? Working { get; init; }
}

/// <summary>
/// What a working day was, for this file: weekends out, and the holidays read from <see cref="HolidaySource"/>, or none.
/// <see cref="HolidayCandidates"/> names the custom tables that look like a holiday list — names only.
/// </summary>
public sealed record WorkingTime(string? HolidaySource, int Holidays, IReadOnlyList<string> HolidayCandidates, int TimeZoneOffsetMinutes);

/// <summary>
/// How cases actually move through the stages, for Enterprise Architecture ONLY (maintainer, 2026-10-12): a workbook
/// and copies of the stage maps with the measured traffic on them, both beside the delivered package and never in it.
///
/// <para>
/// The figures were reduced in the browser before they were saved — counts and durations per stage and per step, no
/// case, customer, product, user or exact time — so nothing here can be traced to a person, and nothing here tries to.
/// </para>
/// </summary>
public static class ProcessMiningStage
{
    public const string Format = "crm-process-mining-export/1";

    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>
    /// Copies <c>Run:ProcessMiningFile</c> into the run as evidence when set; then reads the run's copy, which a
    /// reprocessed run inherits from its source run. Null when neither exists.
    /// </summary>
    public static async Task<ProcessMining?> LoadAsync(RunFolder folder, string file, CancellationToken token)
    {
        string path = file.Trim();
        if (path.Length > 0)
        {
            string full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            if (!File.Exists(full))
            {
                throw new InvalidOperationException($"Run:ProcessMiningFile '{full}' bulunamadı.");
            }
            await folder.CopyVerbatimAsync(full, RunPaths.RawProcessMining, token);
        }
        string evidence = folder.PathOf(RunPaths.RawProcessMining);
        return File.Exists(evidence) ? Parse(await File.ReadAllTextAsync(evidence, token)) : null;
    }

    public static ProcessMining Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetString() != Format)
        {
            throw new InvalidDataException($"Süreç dosyası bir {Format} dosyası değil (tools/crm-process-mining-export.js).");
        }
        string? source = null;
        string? chosenBy = null;
        if (root.TryGetProperty("source", out JsonElement found) && found.ValueKind == JsonValueKind.Object)
        {
            source = $"{Text(found, "entity")} ({Text(found, "caseAttribute")} → talep, {Text(found, "stageAttribute")} → aşama)";
            chosenBy = Text(found, "chosenBy");
        }
        List<string> candidates = [.. Rows(root, "candidates").Select(candidate => $"{Text(candidate, "entity")} ({Text(candidate, "caseAttribute")} / "
            + $"{Text(candidate, "stageAttribute")}, {(Number(candidate, "rows") is double rows ? rows.ToString("0", CultureInfo.InvariantCulture) : "?")} satır)")];
        JsonElement window = root.TryGetProperty("window", out JsonElement span) ? span : default;
        WorkingTime? working = root.TryGetProperty("workingTime", out JsonElement time) && time.ValueKind == JsonValueKind.Object
            ? new WorkingTime(Text(time, "holidaySource"), (int)(Number(time, "holidays") ?? 0),
                [.. Rows(time, "holidayCandidates").Select(name => name.GetString()).OfType<string>()], (int)(Number(time, "timeZoneOffsetMinutes") ?? 0))
            : null;
        return new ProcessMining(source, chosenBy, candidates, (int)(Number(root, "minimumGroup") ?? 0),
            window.ValueKind == JsonValueKind.Object ? Text(window, "fromDate") : null,
            window.ValueKind == JsonValueKind.Object ? Text(window, "toDate") : null,
            (int)(Number(root, "cases") ?? 0), (int)(Number(root, "events") ?? 0), (int)(Number(root, "skipped") ?? 0),
            [.. Rows(root, "stages").Select(stage => new MinedStage(Guid.Parse(Text(stage, "stage")!), (int)(Number(stage, "entries") ?? 0),
                (int)(Number(stage, "lastRecorded") ?? 0)))],
            [.. Rows(root, "transitions").Select(step => new MinedStep(Guid.Parse(Text(step, "from")!), Guid.Parse(Text(step, "to")!),
                (int)(Number(step, "count") ?? 0), Number(step, "meanHours"), Number(step, "medianHours"), Number(step, "p90Hours"))
            {
                MeanWorkDays = Number(step, "meanWorkDays"),
                MedianWorkDays = Number(step, "medianWorkDays"),
                P90WorkDays = Number(step, "p90WorkDays")
            })])
        {
            Working = working
        };
    }

    /// <summary>
    /// The internal workbook and the maps with the traffic on them. Nothing is written into <c>bpmn/</c>: the delivered
    /// maps stay as they were drawn.
    /// </summary>
    public static async Task WriteAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, ProcessMining mining,
        ILogger logger, CancellationToken token)
    {
        List<Sheet> sheets = [Guides.ProcessMining(mining, state.StageMapPaths.Count),
            ProcessMiningReport.Entries(mining, state.CaseStages), ProcessMiningReport.Steps(mining, state.CaseStages)];
        await folder.WriteBytesAsync(RunPaths.ProcessMiningWorkbook, ExcelWorkbook.Build(sheets), token);
        state.Counts["processMining.cases"] = mining.Cases;
        state.Counts["processMining.steps"] = mining.Steps.Count;
        if (mining.Source is not null && state.CaseStages.Count > 0)
        {
            StageTraffic traffic = new(mining.Stages.ToDictionary(stage => stage.Stage, stage => stage.Entries),
                mining.Steps.ToDictionary(step => (step.From, step.To), Caption));
            state.Counts["processMining.maps"] = await StageMapStage.DrawTrafficAsync(folder, state, documents, traffic, logger, token);
        }
        logger.LogInformation("Process mining: {Cases} cases, {Steps} kinds of step from {Source}", mining.Cases, mining.Steps.Count, mining.Source ?? "(no history found)");
    }

    /// <summary>What a step's arrow says on the internal map: how often, and the median when there are enough to have one.</summary>
    public static string Caption(MinedStep step)
    {
        string count = step.Count.ToString("N0", Turkish) + " kez";
        if (step.MedianHours is not double median)
        {
            return count;
        }
        string caption = count + " · medyan " + Duration(median);
        return step.MedianWorkDays is double days ? caption + " (" + days.ToString("0.##", Turkish) + " iş günü)" : caption;
    }

    /// <summary>Hours up to two days, days beyond: "7 sa", "3,5 gün".</summary>
    public static string Duration(double hours)
    {
        return hours < 48 ? hours.ToString("0.#", Turkish) + " sa" : (hours / 24).ToString("0.#", Turkish) + " gün";
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static double? Number(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];
    }
}
