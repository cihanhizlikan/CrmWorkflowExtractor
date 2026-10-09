using System.Text.Json;

namespace Crm.Extract.Metadata;

/// <summary>A Business Process Flow stage (§3.4).</summary>
public sealed record ProcessStage(Guid StageId, Guid? ProcessId, string Name, int? Category, string? PrimaryEntity);

/// <summary>
/// The <c>processstages</c> rows the export kept whole, and the named index derived from them. Every field travels,
/// because what reads a stage's steps out of <c>clientdata</c> is written once its real shape has been seen.
/// </summary>
public static class ProcessStageIndex
{
    public const string IndexFile = Runs.RunPaths.Raw + "/surec-asamalari.json";

    /// <summary>The index these rows make: one named stage per row, in a stable order.</summary>
    public static IReadOnlyList<ProcessStage> Index(IEnumerable<JsonElement> rows)
    {
        return [.. rows.Select(Parse).OfType<ProcessStage>().OrderBy(stage => stage.ProcessId).ThenBy(stage => stage.StageId)];
    }

    /// <summary>One <c>processstages</c> record, or null when it has no id.</summary>
    public static ProcessStage? Parse(JsonElement row)
    {
        return Json.OptionalGuid(row, "processstageid") is Guid id
            ? new ProcessStage(id, Json.OptionalGuid(row, "_processid_value"), Json.OptionalString(row, "stagename") ?? "",
                Json.OptionalInt(row, "stagecategory"), Json.OptionalString(row, "primaryentitytypecode"))
            : null;
    }
}
